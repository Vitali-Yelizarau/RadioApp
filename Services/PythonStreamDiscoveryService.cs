using Newtonsoft.Json;
using RadioApp.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    /// <summary>
    /// Runs the external stream_parser.exe and returns the discovered stream
    /// as DiscoveredRadioStream.
    ///
    /// Expected layout next to the host .exe:
    ///   MyRadioApp.exe
    ///   stream_parser\
    ///       stream_parser.exe
    ///       _internal\
    ///       ms-playwright\
    ///           chromium-XXXX\
    /// </summary>
    public class PythonStreamDiscoveryService
    {
        // Maximum number of additional candidates included in the Description field
        private const int MAX_POSSIBLE_CANDIDATES = 50;

        // Stream parser executable name
        private const string ParserExeName = "stream_parser.exe";

        // Full path to stream_parser.exe, resolved once at construction time
        private readonly string _parserExePath;

        public PythonStreamDiscoveryService(string parserExePath = null)
        {
            _parserExePath = parserExePath ?? ResolveParserExePath();
        }

        /// <summary>
        /// Runs the parser for the given URL and returns the result.
        /// Throws InvalidOperationException if the exe is not found or no stream is discovered.
        /// </summary>
        public async Task<DiscoveredRadioStream> DiscoverAsync(
            string pageUrl,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pageUrl))
                throw new ArgumentException("Page URL is required.", nameof(pageUrl));

            if (string.IsNullOrWhiteSpace(_parserExePath) || !File.Exists(_parserExePath))
                throw new InvalidOperationException(
                    $"Stream parser executable not found: {_parserExePath ?? ParserExeName}");

            Log.Information(
                "[PythonParser] Starting discovery. Url={Url}, Timeout={Timeout}s, Exe={Exe}",
                pageUrl, timeoutSeconds, _parserExePath);

            string jsonOutput = await RunParserAsync(pageUrl, timeoutSeconds, cancellationToken);

            if (string.IsNullOrWhiteSpace(jsonOutput))
                throw new InvalidOperationException("Stream parser returned empty output.");

            ParserResult result;
            try
            {
                result = JsonConvert.DeserializeObject<ParserResult>(jsonOutput);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[PythonParser] Failed to deserialize output: {Output}", jsonOutput);
                throw new InvalidOperationException(
                    "Stream parser returned invalid JSON: " + ex.Message);
            }

            if (result == null)
                throw new InvalidOperationException("Stream parser returned null result.");

            if (!result.Success || result.Candidates == null || result.Candidates.Count == 0)
            {
                string error = result.Error ?? "No playable stream candidates found.";
                Log.Warning("[PythonParser] Discovery failed. Url={Url}, Error={Error}", pageUrl, error);
                throw new InvalidOperationException(error);
            }

            // Take the best candidate (already sorted by quality score, then confidence)
            StreamCandidate best = result.Candidates[0];

            // Prefer the short stable url over finalUrl for display and playback.
            // finalUrl may contain expiring tokens or long redirect chains.
            string streamUrl = !string.IsNullOrWhiteSpace(best.Url)
                ? best.Url
                : best.FinalUrl;

            string description = BuildDescription(best);

            // Append finalUrl to description if it differs from url (redirect target)
            if (!string.IsNullOrWhiteSpace(best.FinalUrl) &&
                !string.Equals(best.Url, best.FinalUrl, StringComparison.OrdinalIgnoreCase))
            {
                description += Environment.NewLine + "  Redirect target: " + best.FinalUrl;
            }

            // Collect additional candidates into Description, one per line.
            // Each candidate shows its url and (if different) finalUrl on the next line.
            if (result.Candidates.Count > 1)
            {
                var extraLines = new List<string>();

                foreach (var c in result.Candidates.Skip(1).Take(MAX_POSSIBLE_CANDIDATES))
                {
                    string shortUrl = !string.IsNullOrWhiteSpace(c.Url) ? c.Url : c.FinalUrl;
                    if (string.IsNullOrWhiteSpace(shortUrl))
                        continue;

                    extraLines.Add("  - " + shortUrl);

                    if (!string.IsNullOrWhiteSpace(c.FinalUrl) &&
                        !string.Equals(c.Url, c.FinalUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        extraLines.Add("      Redirect target: " + c.FinalUrl);
                    }
                }

                if (extraLines.Count > 0)
                {
                    description += Environment.NewLine
                        + "Also possible stream candidates:"
                        + Environment.NewLine
                        + string.Join(Environment.NewLine, extraLines);
                }
            }

            var discovered = new DiscoveredRadioStream
            {
                PageUrl = result.InputUrl ?? pageUrl,
                StreamUrl = streamUrl,
                StationName = result.Title ?? string.Empty,
                Description = description,
                Genre = string.Empty,
                Bitrate = TryParseBitrate(best),
                ContentType = best.ContentType ?? string.Empty
            };

            Log.Information(
                "[PythonParser] Discovery succeeded. Url={Url}, StreamUrl={StreamUrl}, Source={Source}",
                pageUrl, streamUrl, best.Source);

            return discovered;
        }

        // ----------------------------------------------------------------
        // Process execution
        // ----------------------------------------------------------------

        private async Task<string> RunParserAsync(
            string pageUrl,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            // Run stream_parser.exe from its own directory so it can find
            // _internal/ and ms-playwright/ relative to itself
            string workingDir = Path.GetDirectoryName(_parserExePath)
                ?? AppDomain.CurrentDomain.BaseDirectory;

            string args = $"--url \"{pageUrl.Replace("\"", "\\\"")}\" --timeout {timeoutSeconds} --debug";

            var psi = new ProcessStartInfo
            {
                FileName = _parserExePath,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            Log.Debug(
                "[PythonParser] Running: {Exe} {Args} in {Dir}",
                _parserExePath, args, workingDir);

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        stdoutBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        stderrBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for exit with CancellationToken support
                await WaitForExitAsync(process, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(); } catch { }
                    cancellationToken.ThrowIfCancellationRequested();
                }

                string stderr = stderrBuilder.ToString();

                // Write debug log next to stream_parser.exe
                TryWriteDebugLog(_parserExePath, pageUrl, stderr);

                if (!string.IsNullOrWhiteSpace(stderr))
                    Log.Debug("[PythonParser] stderr: {Stderr}", stderr);

                if (process.ExitCode != 0)
                {
                    Log.Warning(
                        "[PythonParser] Process exited with code {Code}. Stderr: {Stderr}",
                        process.ExitCode, stderr);
                }

                return stdoutBuilder.ToString().Trim();
            }
        }

        private static Task WaitForExitAsync(Process process, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => tcs.TrySetResult(true);

            if (ct.CanBeCanceled)
                ct.Register(() => tcs.TrySetCanceled());

            // Handle case where process already exited before we subscribed
            if (process.HasExited)
                tcs.TrySetResult(true);

            return tcs.Task;
        }

        // ----------------------------------------------------------------
        // Debug logging
        // ----------------------------------------------------------------

        /// <summary>
        /// Writes the parser stderr output to a rolling daily log file
        /// placed next to stream_parser.exe:
        ///   stream_parser_YYYY-MM-DD.log
        /// Silently ignored if writing fails.
        /// </summary>
        private static void TryWriteDebugLog(string parserExePath, string pageUrl, string stderr)
        {
            if (string.IsNullOrWhiteSpace(stderr))
                return;

            try
            {
                string logDir = Path.GetDirectoryName(parserExePath)
                    ?? AppDomain.CurrentDomain.BaseDirectory;

                string date = DateTime.Now.ToString("yyyy-MM-dd");
                string logFile = Path.Combine(logDir, $"stream_parser_{date}.log");

                var sb = new StringBuilder();
                sb.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} | URL: {pageUrl} ===");
                sb.AppendLine(stderr.TrimEnd());
                sb.AppendLine();

                File.AppendAllText(logFile, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PythonParser] Failed to write debug log.");
            }
        }

        // ----------------------------------------------------------------
        // Path resolution
        // ----------------------------------------------------------------

        /// <summary>
        /// Searches for stream_parser.exe in the following locations:
        ///   1. stream_parser\ subfolder next to the host .exe  (recommended layout)
        ///   2. Directly next to the host .exe
        ///   3. Directories listed in PATH
        /// </summary>
        private static string ResolveParserExePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. stream_parser\ subfolder — preferred PyInstaller onedir layout
            string subFolder = Path.Combine(baseDir, "stream_parser", ParserExeName);
            if (File.Exists(subFolder))
            {
                Log.Debug("[PythonParser] Found parser at: {Path}", subFolder);
                return subFolder;
            }

            // 2. Next to the host .exe
            string sameDir = Path.Combine(baseDir, ParserExeName);
            if (File.Exists(sameDir))
            {
                Log.Debug("[PythonParser] Found parser at: {Path}", sameDir);
                return sameDir;
            }

            // 3. PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(dir, ParserExeName);
                    if (File.Exists(candidate))
                    {
                        Log.Debug("[PythonParser] Found parser in PATH: {Path}", candidate);
                        return candidate;
                    }
                }
                catch { }
            }

            Log.Warning("[PythonParser] {ExeName} not found.", ParserExeName);
            return null;
        }

        // ----------------------------------------------------------------
        // Helper methods
        // ----------------------------------------------------------------

        private static string BuildDescription(StreamCandidate candidate)
        {
            if (candidate == null) return string.Empty;

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(candidate.ContentType))
                parts.Add(candidate.ContentType.Split(';')[0].Trim());

            if (!string.IsNullOrWhiteSpace(candidate.QualityHint) &&
                !candidate.QualityHint.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                parts.Add(candidate.QualityHint.ToUpperInvariant());

            if (!string.IsNullOrWhiteSpace(candidate.Source))
                parts.Add("via " + candidate.Source);

            return parts.Count > 0
                ? string.Join(" · ", parts)
                : "Detected by stream parser.";
        }

        private static int? TryParseBitrate(StreamCandidate candidate)
        {
            // qualityHint can be "hd", "high", "standard" — not a bitrate value
            // Not parsed for now, can be extended if needed
            return null;
        }

        // ----------------------------------------------------------------
        // JSON DTO classes
        // ----------------------------------------------------------------

        private class ParserResult
        {
            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("inputUrl")]
            public string InputUrl { get; set; }

            [JsonProperty("effectiveUrl")]
            public string EffectiveUrl { get; set; }

            [JsonProperty("title")]
            public string Title { get; set; }

            [JsonProperty("candidates")]
            public List<StreamCandidate> Candidates { get; set; }

            [JsonProperty("error")]
            public string Error { get; set; }
        }

        private class StreamCandidate
        {
            [JsonProperty("url")]
            public string Url { get; set; }

            [JsonProperty("finalUrl")]
            public string FinalUrl { get; set; }

            [JsonProperty("source")]
            public string Source { get; set; }

            [JsonProperty("confidence")]
            public int Confidence { get; set; }

            [JsonProperty("qualityHint")]
            public string QualityHint { get; set; }

            [JsonProperty("qualityScore")]
            public int QualityScore { get; set; }

            [JsonProperty("contentType")]
            public string ContentType { get; set; }

            [JsonProperty("isPlayable")]
            public bool IsPlayable { get; set; }

            [JsonProperty("isTemporary")]
            public bool IsTemporary { get; set; }
        }
    }
}
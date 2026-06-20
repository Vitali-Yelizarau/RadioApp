using RadioApp.Models;
using RadioApp.Services;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RadioApp
{
    public partial class AddStationWindow : Window
    {
        const int MIN_TIMEOUT = 3;
        const int MAX_TIMEOUT = 999;

        // Stream parser executable name — expected next to the host .exe
        private const string ParserExeName = "stream_parser.exe";

        // Fallback: GitHub repository URL shown when the parser is not found
        private const string ParserRepositoryUrl = "https://github.com/Vitali-Yelizarau/StreamURL_Parser";

        // Use the Python-based stream parser instead of the old C# service
        private readonly PythonStreamDiscoveryService _discoveryService;
        private readonly RadioStreamInfoService _streamInfoService;

        private readonly StationWindowMode _mode;
        private readonly MediaItem _editingItem;

        private string Description
        {
            get
            {
                return DescriptionTextBox.Text.Trim();
            }
            set
            {
                DescriptionTextBox.Text = value ?? string.Empty;
            }
        }

        public DiscoveredRadioStream DiscoveredStream { get; private set; }
        public string UserTitle
        {
            get { return TitleTextBox.Text.Trim(); }
        }
        public string PageUrl
        {
            get { return PageUrlTextBox.Text.Trim(); }
        }

        public string StreamUrl
        {
            get { return StreamUrlTextBox.Text.Trim(); }
        }

        public string UserDescription
        {
            get { return DescriptionTextBox.Text.Trim(); }
        }

        private int TimeoutSeconds
        {
            get
            {
                if (!int.TryParse(TimeoutTextBox.Text.Trim(), out int seconds))
                {
                    throw new InvalidOperationException("Timeout must be a number.");
                }

                if (seconds < MIN_TIMEOUT)
                {
                    throw new InvalidOperationException($"Timeout must be at least {MIN_TIMEOUT} seconds.");
                }

                if (seconds > MAX_TIMEOUT)
                {
                    throw new InvalidOperationException($"Timeout must not be greater than {MAX_TIMEOUT} seconds.");
                }

                return seconds;
            }
        }

        public MediaItem UpdatedItem
        {
            get
            {
                if (_editingItem == null)
                {
                    return null;
                }

                return new MediaItem
                {
                    Id = _editingItem.Id,
                    Title = UserTitle,
                    Description = UserDescription,
                    SourceType = _editingItem.SourceType,
                    WebsiteUrl = _editingItem.WebsiteUrl,
                    StreamUrl = StreamUrl,
                    Genre = _editingItem.Genre,
                    SortOrder = _editingItem.SortOrder,
                    IsEnabled = _editingItem.IsEnabled
                };
            }
        }

        public AddStationWindow()
        {
            InitializeComponent();
            InitializeTooltips();

            _mode = StationWindowMode.Add;

            _discoveryService = new PythonStreamDiscoveryService();
            _streamInfoService = new RadioStreamInfoService();

            DiscoveredStream = new DiscoveredRadioStream();

            ConfigureAddMode();
        }

        public AddStationWindow(MediaItem item)
        {
            InitializeComponent();

            _mode = StationWindowMode.Edit;
            _editingItem = item ?? throw new ArgumentNullException(nameof(item));

            _discoveryService = new PythonStreamDiscoveryService();
            _streamInfoService = new RadioStreamInfoService();

            DiscoveredStream = new DiscoveredRadioStream
            {
                PageUrl = item.WebsiteUrl ?? string.Empty,
                StreamUrl = item.StreamUrl ?? string.Empty,
                StationName = item.Title ?? string.Empty,
                Description = item.Description ?? string.Empty,
                Genre = item.Genre ?? string.Empty
            };

            ConfigureEditMode(item);
        }

        private void ConfigureAddMode()
        {
            Title = "Add radio station";

            PageUrlTextBox.IsEnabled = true;
            StreamUrlTextBox.IsEnabled = true;
            TimeoutTextBox.IsEnabled = true;
            DescriptionTextBox.IsEnabled = true;
            TitleTextBox.IsEnabled = true;

            FindStreamUrlButton.Visibility = Visibility.Visible;
            AddButton.Content = "Add";
        }

        private void ConfigureEditMode(MediaItem item)
        {
            Title = "Update radio station";

            PageUrlTextBox.Text = item.WebsiteUrl ?? string.Empty;
            StreamUrlTextBox.Text = item.StreamUrl ?? string.Empty;
            DescriptionTextBox.Text = item.Description ?? string.Empty;

            TitleTextBox.Text = item.Title ?? string.Empty;
            TitleTextBox.IsEnabled = true;

            TimeoutTextBox.Text = "0";
            TimeoutTextBox.IsEnabled = false;

            PageUrlTextBox.IsEnabled = false;
            StreamUrlTextBox.IsEnabled = true;
            DescriptionTextBox.IsEnabled = true;

            FindStreamUrlButton.Visibility = Visibility.Collapsed;
            AddButton.Content = "Update";
        }

        private async void FindStreamUrlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusyState(true, "Checking...");

                // Check that the parser executable is available before attempting discovery
                if (!IsParserAvailable())
                {
                    PromptParserNotFound();
                    return;
                }

                if (!string.IsNullOrWhiteSpace(StreamUrl))
                {
                    DiscoveredStream = PrepareStationFromDirectStreamUrl();
                    return;
                }

                if (string.IsNullOrWhiteSpace(PageUrl))
                {
                    throw new InvalidOperationException(
                        "Please enter Radio page URL or direct Stream URL."
                    );
                }

                DiscoveredStream = await RunWithTimeoutAsync(
                                                token => DetectStreamFromPageAsync(PageUrl, token),
                                                TimeoutSeconds
                                            );
            }
            catch (TimeoutException ex)
            {
                Log.Warning(
                    ex,
                    "Stream detection timed out. PageUrl: {PageUrl}, StreamUrl: {StreamUrl}, TimeoutSeconds: {TimeoutSeconds}",
                    PageUrl,
                    StreamUrl,
                    SafeGetTimeoutSeconds()
                );

                MessageBox.Show(
                    ex.Message,
                    "Operation timed out",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Stream detection failed. PageUrl: {PageUrl}, StreamUrl: {StreamUrl}",
                    PageUrl,
                    StreamUrl
                );

                MessageBox.Show(
                    ex.Message,
                    "Stream check failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            finally
            {
                SetBusyState(false, null);
            }
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mode == StationWindowMode.Add)
                {
                    SetBusyState(true, "Adding...");

                    if (CanUseAlreadyDiscoveredStream())
                    {
                        DialogResult = true;
                        Close();
                        return;
                    }

                    DiscoveredStream = await RunWithTimeoutAsync(
                                            token => PrepareStationForAddAsync(token),
                                            TimeoutSeconds
                                        );

                    DialogResult = true;
                    Close();
                }
                else
                {
                    SetBusyState(true, "Updating...");

                    PrepareStationForUpdate();

                    DialogResult = true;
                    Close();
                }
            }
            catch (TimeoutException ex)
            {
                Log.Warning(
                    ex,
                    "Station action timed out. Mode: {Mode}, PageUrl: {PageUrl}, StreamUrl: {StreamUrl}, TimeoutSeconds: {TimeoutSeconds}",
                    _mode,
                    PageUrl,
                    StreamUrl,
                    SafeGetTimeoutSeconds()
                );

                MessageBox.Show(
                    ex.Message,
                    "Operation timed out",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Station window action failed. Mode: {Mode}, PageUrl: {PageUrl}, StreamUrl: {StreamUrl}",
                    _mode,
                    PageUrl,
                    StreamUrl
                );

                MessageBox.Show(
                    ex.Message,
                    _mode == StationWindowMode.Add ? "Add station failed" : "Update station failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                SetBusyState(false, null);
            }
        }

        private async Task<T> RunWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> operationFactory, int timeoutSeconds)
        {
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                Task<T> operationTask = operationFactory(cancellationTokenSource.Token);
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

                Task completedTask = await Task.WhenAny(operationTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    cancellationTokenSource.Cancel();

                    throw new TimeoutException(
                        "The operation timed out and was cancelled."
                    );
                }

                return await operationTask;
            }
        }

        private async Task<DiscoveredRadioStream> PrepareStationForAddAsync(CancellationToken cancellationToken)
        {
            bool hasPageUrl = !string.IsNullOrWhiteSpace(PageUrl);
            bool hasStreamUrl = !string.IsNullOrWhiteSpace(StreamUrl);

            if (!hasPageUrl && !hasStreamUrl)
            {
                throw new InvalidOperationException(
                    "Please enter either Radio page URL or Stream URL."
                );
            }

            if (hasStreamUrl)
            {
                return PrepareStationFromDirectStreamUrl();
            }

            // Page URL provided — check parser availability before launching
            if (!IsParserAvailable())
            {
                PromptParserNotFound();
                throw new InvalidOperationException(
                    "Stream parser is not available. Please download it from the repository."
                );
            }

            return await DetectStreamFromPageAsync(PageUrl, cancellationToken);
        }

        private DiscoveredRadioStream PrepareStationFromDirectStreamUrl()
        {
            string streamUrl = StreamUrl.Trim();

            if (!Uri.IsWellFormedUriString(streamUrl, UriKind.Absolute))
            {
                throw new InvalidOperationException(
                    "The entered Stream URL is not a valid absolute URL."
                );
            }

            string title = Title;

            if (string.IsNullOrWhiteSpace(title))
            {
                title = BuildTitleFromStreamUrl(streamUrl);
            }

            string description = Description;

            if (string.IsNullOrWhiteSpace(description))
            {
                description = "Added manually from direct Stream URL.";
            }

            var result = new DiscoveredRadioStream
            {
                PageUrl = string.IsNullOrWhiteSpace(PageUrl) ? string.Empty : PageUrl.Trim(),
                StreamUrl = streamUrl,
                StationName = title,
                Description = description,
                Genre = string.Empty,
                Bitrate = null
            };

            ApplyResultToForm(result);

            Log.Information(
                "Station prepared from direct Stream URL. PageUrl: {PageUrl}, StreamUrl: {StreamUrl}, StationName: {StationName}",
                result.PageUrl,
                result.StreamUrl,
                result.StationName
            );

            return result;
        }

        private string BuildTitleFromStreamUrl(string streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                return "New radio station";
            }

            Uri uri;

            if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out uri))
            {
                return "New radio station";
            }

            string[] parts = uri.AbsolutePath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            string candidate = parts
                .Reverse()
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    !x.Equals("stream", StringComparison.OrdinalIgnoreCase) &&
                    !x.Equals("live", StringComparison.OrdinalIgnoreCase) &&
                    !x.StartsWith("mp3", StringComparison.OrdinalIgnoreCase) &&
                    !x.StartsWith("aac", StringComparison.OrdinalIgnoreCase)
                );

            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = uri.Host;
            }

            candidate = candidate
                .Replace("-", " ")
                .Replace("_", " ")
                .Trim();

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return "New radio station";
            }

            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(candidate);
        }

        private void PrepareStationForUpdate()
        {
            string streamUrl = StreamUrlTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                throw new InvalidOperationException("Stream URL is required.");
            }

            DiscoveredStream = new DiscoveredRadioStream
            {
                PageUrl = PageUrl,
                StreamUrl = streamUrl,
                StationName = _editingItem.Title,
                Description = UserDescription,
                Genre = _editingItem.Genre
            };
        }

        private async Task<DiscoveredRadioStream> DetectStreamFromPageAsync(
            string pageUrl,
            CancellationToken cancellationToken)
        {
            Log.Information(
                "Stream URL is empty. Trying to detect stream from page URL: {PageUrl}",
                pageUrl);

            DiscoveredRadioStream result = await _discoveryService.DiscoverAsync(
                pageUrl,
                TimeoutSeconds,
                cancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.StreamUrl))
            {
                throw new InvalidOperationException(
                    "Stream URL could not be detected from this page.");
            }

            ApplyResultToForm(result);

            // Try to enrich station name and description from ICY headers or page meta tags
            await TryEnrichStationInfoAsync(result, cancellationToken);

            Log.Information(
                "Stream detected from page. PageUrl: {PageUrl}, StreamUrl: {StreamUrl}, StationName: {StationName}",
                result.PageUrl,
                result.StreamUrl,
                result.StationName);

            return result;
        }

        /// <summary>
        /// Tries to enrich the discovered stream with station name and description.
        ///
        /// Priority order:
        ///   1. HTML meta tags from the page URL (og:title, og:description, etc.)
        ///      — most human-readable source
        ///   2. ICY headers from the stream URL (icy-name, icy-description)
        ///      — fallback; often contains technical IDs like "ROCK_RADIO"
        ///
        /// The parser-generated description (audio/mpeg · via ...) is kept only
        /// if neither meta tags nor ICY provide a better description.
        /// Only fills fields that are still empty — never overwrites user input.
        /// </summary>
        private async Task TryEnrichStationInfoAsync(
            DiscoveredRadioStream result,
            CancellationToken cancellationToken)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.StreamUrl))
                return;

            // Step 1: HTML meta tags from the page — best human-readable source
            if (!string.IsNullOrWhiteSpace(result.PageUrl))
            {
                try
                {
                    var metaInfo = await TryGetMetaTagInfoAsync(result.PageUrl, cancellationToken);

                    if (metaInfo != null)
                    {
                        if (!string.IsNullOrWhiteSpace(metaInfo.Item1))
                        {
                            result.StationName = metaInfo.Item1;
                            Log.Debug("Enriched station name from meta tags: {Name}", result.StationName);
                        }

                        if (!string.IsNullOrWhiteSpace(metaInfo.Item2))
                        {
                            // Prepend human-readable description before the technical parser info
                            if (!string.IsNullOrWhiteSpace(result.Description))
                            {
                                result.Description = metaInfo.Item2
                                    + Environment.NewLine + Environment.NewLine
                                    + result.Description;
                            }
                            else
                            {
                                result.Description = metaInfo.Item2;
                            }
                            Log.Debug("Enriched description from meta tags: {Desc}", result.Description);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Meta tag enrichment failed for: {PageUrl}", result.PageUrl);
                }
            }

            // Step 2: ICY headers — fallback if meta tags gave nothing
            if (string.IsNullOrWhiteSpace(result.StationName) ||
                string.IsNullOrWhiteSpace(result.Description))
            {
                try
                {
                    DiscoveredRadioStream icyInfo = await _streamInfoService
                        .GetStreamInfoIfPlayableAsync(result.PageUrl, result.StreamUrl, 8);

                    if (icyInfo != null)
                    {
                        if (string.IsNullOrWhiteSpace(result.StationName) &&
                            !string.IsNullOrWhiteSpace(icyInfo.StationName))
                        {
                            result.StationName = icyInfo.StationName;
                            Log.Debug("Enriched station name from ICY: {Name}", result.StationName);
                        }

                        if (!string.IsNullOrWhiteSpace(icyInfo.Description))
                        {
                            // Prepend ICY description before the technical parser info
                            if (!string.IsNullOrWhiteSpace(result.Description))
                            {
                                result.Description = icyInfo.Description
                                    + Environment.NewLine + Environment.NewLine
                                    + result.Description;
                            }
                            else
                            {
                                result.Description = icyInfo.Description;
                            }
                            Log.Debug("Enriched description from ICY: {Desc}", result.Description);
                        }

                        if (string.IsNullOrWhiteSpace(result.Genre) &&
                            !string.IsNullOrWhiteSpace(icyInfo.Genre))
                        {
                            result.Genre = icyInfo.Genre;
                        }

                        if (result.Bitrate == null && icyInfo.Bitrate != null)
                        {
                            result.Bitrate = icyInfo.Bitrate;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ICY enrichment failed for: {StreamUrl}", result.StreamUrl);
                }
            }

            // Re-apply enriched data to form fields, overwriting
            // the technical parser text from the initial ApplyResultToForm call
            ApplyEnrichedResultToForm(result);
        }

        /// <summary>
        /// Force-overwrites form fields with enriched data.
        /// Unlike ApplyResultToForm, this method does NOT check if the field is empty —
        /// it always writes the enriched values, since they were already merged
        /// (e.g. human description prepended before the technical text).
        /// </summary>
        private void ApplyEnrichedResultToForm(DiscoveredRadioStream result)
        {
            if (result == null)
                return;

            if (!string.IsNullOrWhiteSpace(result.StreamUrl))
                StreamUrlTextBox.Text = result.StreamUrl;

            if (!string.IsNullOrWhiteSpace(result.StationName))
                TitleTextBox.Text = CleanDisplayText(result.StationName);

            if (!string.IsNullOrWhiteSpace(result.Description))
                DescriptionTextBox.Text = CleanDisplayText(result.Description);
        }

        /// <summary>
        /// Downloads the page and extracts title and description from HTML meta tags.
        /// Returns (title, description) or null if nothing useful was found.
        /// Checks: og:title, og:description, twitter:title, twitter:description,
        ///         meta[name=description], and &lt;title&gt; tag.
        /// </summary>
        private async Task<Tuple<string, string>> TryGetMetaTagInfoAsync(
            string pageUrl,
            CancellationToken cancellationToken)
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 RadioApp/1.0");

                string html;
                try
                {
                    html = await client.GetStringAsync(pageUrl);
                }
                catch
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(html))
                    return null;

                string title = null;
                string description = null;

                // og:title / og:description / twitter:title / twitter:description
                var metaPattern = new System.Text.RegularExpressions.Regex(
                    @"<meta\s+[^>]*(?:property|name)\s*=\s*[""']([^""']+)[""'][^>]*content\s*=\s*[""']([^""']*)[""']|" +
                    @"<meta\s+[^>]*content\s*=\s*[""']([^""']*)[""'][^>]*(?:property|name)\s*=\s*[""']([^""']+)[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                foreach (System.Text.RegularExpressions.Match m in metaPattern.Matches(html))
                {
                    string propName = !string.IsNullOrWhiteSpace(m.Groups[1].Value)
                        ? m.Groups[1].Value.ToLowerInvariant()
                        : m.Groups[4].Value.ToLowerInvariant();

                    string propValue = !string.IsNullOrWhiteSpace(m.Groups[2].Value)
                        ? m.Groups[2].Value
                        : m.Groups[3].Value;

                    propValue = WebUtility.HtmlDecode(propValue.Trim());

                    if (string.IsNullOrWhiteSpace(propValue))
                        continue;

                    if (title == null &&
                        (propName == "og:title" || propName == "twitter:title"))
                    {
                        title = propValue;
                    }

                    if (description == null &&
                        (propName == "og:description" ||
                         propName == "twitter:description" ||
                         propName == "description" ||
                         propName == "desc"))
                    {
                        description = propValue;
                    }
                }

                // Fallback: <title> tag
                if (title == null)
                {
                    var titleMatch = System.Text.RegularExpressions.Regex.Match(
                        html,
                        @"<title[^>]*>([^<]+)</title>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (titleMatch.Success)
                    {
                        title = WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                    }
                }

                if (title == null && description == null)
                    return null;

                return Tuple.Create(title ?? string.Empty, description ?? string.Empty);
            }
        }

        private void ApplyResultToForm(DiscoveredRadioStream result)
        {
            if (!string.IsNullOrWhiteSpace(result.StreamUrl))
            {
                StreamUrlTextBox.Text = result.StreamUrl;
            }

            if (string.IsNullOrWhiteSpace(TitleTextBox.Text) &&
                !string.IsNullOrWhiteSpace(result.StationName))
            {
                TitleTextBox.Text = CleanDisplayText(result.StationName);
            }

            if (string.IsNullOrWhiteSpace(DescriptionTextBox.Text))
            {
                if (!string.IsNullOrWhiteSpace(result.Description))
                {
                    DescriptionTextBox.Text = CleanDisplayText(result.Description);
                }
                else if (!string.IsNullOrWhiteSpace(result.StationName))
                {
                    DescriptionTextBox.Text = CleanDisplayText(result.StationName);
                }
            }
        }

        /// <summary>
        /// Returns true if the stream parser executable is present next to the host .exe
        /// or anywhere on the system PATH.
        /// </summary>
        private bool IsParserAvailable()
        {
            // Check next to the running .exe first
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(exeDir, ParserExeName);

            if (File.Exists(localPath))
            {
                return true;
            }

            // Also check inside stream_parser sub-folder (PyInstaller onedir layout)
            string subFolderPath = Path.Combine(exeDir, "stream_parser", ParserExeName);

            if (File.Exists(subFolderPath))
            {
                return true;
            }

            // Finally check PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, ParserExeName)))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Skip invalid PATH entries
                }
            }

            return false;
        }

        /// <summary>
        /// Shows a message box informing the user that the parser is missing
        /// and offers to open the GitHub repository.
        /// </summary>
        private void PromptParserNotFound()
        {
            Log.Warning(
                "Stream parser executable not found. Expected: {ParserExeName}",
                ParserExeName);

            MessageBoxResult choice = MessageBox.Show(
                $"Stream parser ({ParserExeName}) was not found.\n\n" +
                $"Please download it from the repository and place it next to the application.\n\n" +
                $"Open the repository now?",
                "Stream parser not found",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (choice == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = ParserRepositoryUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open repository URL: {Url}", ParserRepositoryUrl);
                }
            }
        }

        private int SafeGetTimeoutSeconds()
        {
            int seconds;

            if (int.TryParse(TimeoutTextBox.Text.Trim(), out seconds))
            {
                return seconds;
            }

            return -1;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SetBusyState(bool isBusy, string activeButtonText)
        {
            bool isEditMode = _mode == StationWindowMode.Edit;

            PageUrlTextBox.IsEnabled = !isBusy && !isEditMode;
            StreamUrlTextBox.IsEnabled = !isBusy;
            TimeoutTextBox.IsEnabled = !isBusy && !isEditMode;
            DescriptionTextBox.IsEnabled = !isBusy;

            FindStreamUrlButton.IsEnabled = !isBusy;
            StreamUrlHelpButton.IsEnabled = !isBusy;
            CancelButton.IsEnabled = !isBusy;
            AddButton.IsEnabled = !isBusy;
            TitleTextBox.IsEnabled = !isBusy;

            if (isBusy)
            {
                if (activeButtonText == "Checking...")
                {
                    FindStreamUrlButton.Content = "Checking...";
                }
                else if (activeButtonText == "Adding...")
                {
                    AddButton.Content = "Adding...";
                }
                else if (activeButtonText == "Updating...")
                {
                    AddButton.Content = "Updating...";
                }
            }
            else
            {
                FindStreamUrlButton.Content = "Find stream URL";
                AddButton.Content = isEditMode ? "Update" : "Add";
            }
        }

        private bool CanUseAlreadyDiscoveredStream()
        {
            if (DiscoveredStream == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(DiscoveredStream.StreamUrl))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(StreamUrl))
            {
                return false;
            }

            string discoveredStreamUrl = NormalizeUrlForComparison(DiscoveredStream.StreamUrl);
            string currentStreamUrl = NormalizeUrlForComparison(StreamUrl);

            if (!string.Equals(discoveredStreamUrl, currentStreamUrl, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(PageUrl) &&
                !string.IsNullOrWhiteSpace(DiscoveredStream.PageUrl))
            {
                string discoveredPageUrl = NormalizeUrlForComparison(DiscoveredStream.PageUrl);
                string currentPageUrl = NormalizeUrlForComparison(PageUrl);

                if (!string.Equals(discoveredPageUrl, currentPageUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            Log.Information(
                "Using already discovered stream without second validation. PageUrl: {PageUrl}, StreamUrl: {StreamUrl}",
                PageUrl,
                StreamUrl
            );

            return true;
        }

        private string NormalizeUrlForComparison(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            return url
                .Trim()
                .TrimEnd('/')
                .ToLowerInvariant();
        }

        private static string CleanDisplayText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            string decoded = WebUtility.HtmlDecode(value);

            return decoded.Trim();
        }

        private void StreamUrlHelpButton_Click(object sender, RoutedEventArgs e)
        {
            const string repoUrl = "https://github.com/Vitali-Yelizarau/StreamURL_Parser#finding-a-stream-url-manually";

            string message =
                "Stream URL is the direct audio endpoint that VLC or any media player can open." + Environment.NewLine +
                "It is NOT the same as the Radio page URL." + Environment.NewLine +
                Environment.NewLine +
                "Example:" + Environment.NewLine +
                "  Radio page URL:  https://example.com/listen" + Environment.NewLine +
                "  Stream URL:      https://stream.example.com/live.mp3" + Environment.NewLine +
                Environment.NewLine +
                "How to find Stream URL manually:" + Environment.NewLine +
                "  1. Open the station's page in Chrome / Edge / Firefox." + Environment.NewLine +
                "  2. Press F12 to open DevTools, switch to the Network tab." + Environment.NewLine +
                "  3. Enable the Media filter and tick \"Preserve log\"." + Environment.NewLine +
                "  4. Click the Play button on the station's page." + Environment.NewLine +
                "  5. Find the new entry with content type audio/mpeg, audio/aac," + Environment.NewLine +
                "     audio/aacp, or a URL ending with .mp3, .aac, .m3u8." + Environment.NewLine +
                "  6. Right-click it -> Copy -> Copy URL." + Environment.NewLine +
                "  7. Paste the URL into the Stream URL field above." + Environment.NewLine +
                Environment.NewLine +
                "Open the full guide on GitHub?";

            MessageBoxResult choice = MessageBox.Show(
                message,
                "How to find Stream URL",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );

            if (choice == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = repoUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open help URL: {Url}", repoUrl);
                }
            }
        }

        private void InitializeTooltips()
        {
            SetTooltip(PageUrlTextBox,
                "Paste the web page of the radio station — the page that has the Play button in your browser.\n" +
                "\n" +
                "Example: https://onlineradiobox.com/mx/radioranchito/\n" +
                "\n" +
                "This is NOT the direct audio stream URL. After the button \"Find stream URL\" was clicked, the parser will analyse this page and try to discover the underlying stream automatically."
            );

            SetTooltip(TitleTextBox,
                "The name that will be shown in your saved stations list.\n" +
                "\n" +
                "You can type it manually, or leave it empty — after a successful parse it will be auto-filled from the page metadata (Open Graph tags, page title) or, as a fallback, from the ICY stream headers.\n" +
                "\n" +
                "If both auto-detection and your input are empty, the station will be saved as \"New radio station\" and you can edit it later."
            );

            SetTooltip(StreamUrlTextBox,
                "The direct audio stream URL that VLC will open for playback.\n" +
                "\n" +
                "This is NOT the same as the Radio page URL — it usually points to a different server (for example: https://streamingcwsradio30.com:7005/stream.mp3).\n" +
                "\n" +
                "Auto-filled after a successful parse. If you know the stream URL already, you can paste it here directly and skip the page parsing.\n" +
                "\n" +
                "Supported formats:\n" +
                "  • Direct audio: MP3, AAC, AACP, OGG, Opus\n" +
                "  • Icecast / Shoutcast endpoints (with or without trailing /stream, /listen, /;)\n" +
                "  • HLS playlists (.m3u8)\n" +
                "\n" +
                "NOT supported: .pls playlist files (these need to be opened manually to copy the inner stream URL).\n" +
                "\n" +
                "Click the ? button to the right for a step-by-step guide on finding stream URLs manually with browser DevTools."
            );

            SetTooltip(DescriptionTextBox,
                "Free-form text shown together with the station. Optional.\n" +
                "\n" +
                "After parsing, this field is auto-filled with two kinds of info:\n" +
                "  • A human-readable description from the page (Open Graph, meta description, page title)\n" +
                "  • Technical details from the parser: content type, extraction method, and alternative stream candidates if multiple were found\n" +
                "\n" +
                "If the auto-detected Stream URL doesn't play, look here for \"Also possible stream candidates:\" — you can copy one of those URLs into the Stream URL field and try again.\n" +
                "\n" +
                "You can keep, edit, or clear this text — it doesn't affect playback."
            );
        }

        private static void SetTooltip(Control control, string text)
        {
            control.ToolTip = text;
            ToolTipService.SetShowDuration(control, 30000);
            ToolTipService.SetInitialShowDelay(control, 500);
        }
    }
}
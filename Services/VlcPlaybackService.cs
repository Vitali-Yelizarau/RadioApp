using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class VlcPlaybackService : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly StreamPlaybackUrlResolver _playbackUrlResolver;

        private readonly string _vlcLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "vlc.log");
        private LibVLC _libVlc;
        private MediaPlayer _mediaPlayer;
        private Media _currentMedia;

        private bool _isInitialized;
        private string _currentOriginalUrl;
        private string _currentPlaybackUrl;

        public event EventHandler PlaybackStarted;
        public event EventHandler PlaybackPaused;
        public event EventHandler PlaybackStopped;
        public event EventHandler<string> PlaybackFailed;

        public VlcPlaybackService()
        {
            _playbackUrlResolver = new StreamPlaybackUrlResolver();
        }

        public bool IsPlaying
        {
            get
            {
                lock (_syncRoot)
                {
                    return _mediaPlayer != null && _mediaPlayer.IsPlaying;
                }
            }
        }

        public async Task PlayAsync(string streamUrl)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                throw new ArgumentException("Stream URL is empty.", nameof(streamUrl));
            }

            var totalWatch = Stopwatch.StartNew();

            Log.Information("VLC PlayAsync started. StreamUrl: {StreamUrl}", streamUrl);

            string playbackUrl = await _playbackUrlResolver.ResolvePlaybackUrlAsync(streamUrl);

            Log.Information(
                "VLC playback URL selected in {ElapsedMilliseconds} ms. OriginalUrl: {OriginalUrl}, PlaybackUrl: {PlaybackUrl}",
                totalWatch.ElapsedMilliseconds,
                streamUrl,
                playbackUrl
            );

            await Task.Run(() =>
            {
                StartPlaybackInternal(streamUrl, playbackUrl);
            });

            totalWatch.Stop();

            Log.Information(
                "VLC PlayAsync finished in {ElapsedMilliseconds} ms. OriginalUrl: {OriginalUrl}, PlaybackUrl: {PlaybackUrl}",
                totalWatch.ElapsedMilliseconds,
                streamUrl,
                playbackUrl
            );
        }

        public void Pause()
        {
            lock (_syncRoot)
            {
                EnsureInitializedInternal();

                if (_mediaPlayer == null)
                {
                    return;
                }

                _mediaPlayer.Pause();

                Log.Information("VLC playback pause requested.");
            }
        }

        public void Resume()
        {
            lock (_syncRoot)
            {
                EnsureInitializedInternal();

                if (_mediaPlayer == null)
                {
                    return;
                }

                _mediaPlayer.Play();

                Log.Information("VLC playback resume requested.");
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                StopInternal();
            }
        }

        public Task InitializeAsync()
        {
            return Task.Run(() =>
            {
                lock (_syncRoot)
                {
                    EnsureInitializedInternal();
                }
            });
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                try
                {
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.Playing -= MediaPlayer_Playing;
                        _mediaPlayer.Paused -= MediaPlayer_Paused;
                        _mediaPlayer.Stopped -= MediaPlayer_Stopped;
                        _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                        _mediaPlayer.EndReached -= MediaPlayer_EndReached;
                        _mediaPlayer.Buffering -= MediaPlayer_Buffering;

                        try
                        {
                            _mediaPlayer.Stop();
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to stop VLC media player during dispose.");
                        }

                        _mediaPlayer.Dispose();
                        _mediaPlayer = null;
                    }

                    DisposeCurrentMedia();

                    if (_libVlc != null)
                    {
                        _libVlc.Log -= LibVlc_Log;
                        _libVlc.Dispose();
                        _libVlc = null;
                    }

                    _isInitialized = false;
                    _currentOriginalUrl = null;
                    _currentPlaybackUrl = null;

                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to dispose VLC playback service safely.");
                }
            }
        }

        private void StartPlaybackInternal(string originalUrl, string playbackUrl)
        {
            var watch = Stopwatch.StartNew();

            lock (_syncRoot)
            {
                Log.Information("VLC StartPlaybackInternal started.");

                EnsureInitializedInternal();

                Log.Information(
                    "VLC EnsureInitialized finished before playback in {ElapsedMilliseconds} ms.",
                    watch.ElapsedMilliseconds
                );

                StopInternal();

                System.Threading.Thread.Sleep(500);

                _currentOriginalUrl = originalUrl;
                _currentPlaybackUrl = playbackUrl;

                Log.Information(
                    "VLC opening stream. OriginalUrl: {OriginalUrl}, PlaybackUrl: {PlaybackUrl}",
                    _currentOriginalUrl,
                    _currentPlaybackUrl
                );

                DisposeCurrentMedia();

                _currentMedia = new Media(_libVlc, new Uri(_currentPlaybackUrl));

                _currentMedia.AddOption(":no-video");
                _currentMedia.AddOption(":network-caching=5000");
                _currentMedia.AddOption(":live-caching=8000");
                _currentMedia.AddOption(":http-reconnect=true");
                //_currentMedia.AddOption(":http-continuous");
                _currentMedia.AddOption(":http-user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");

                bool started = _mediaPlayer.Play(_currentMedia);

                if (!started)
                {
                    throw new InvalidOperationException("LibVLC could not start playback.");
                }

                watch.Stop();

                Log.Information(
                    "VLC StartPlaybackInternal finished in {ElapsedMilliseconds} ms.",
                    watch.ElapsedMilliseconds
                );
            }
        }

        private void EnsureInitializedInternal()
        {
            if (_isInitialized)
            {
                return;
            }

            var watch = Stopwatch.StartNew();

            Log.Information("VLC initialization started.");

            Core.Initialize();

            Log.Information(
                "VLC Core.Initialize finished in {ElapsedMilliseconds} ms.",
                watch.ElapsedMilliseconds
            );

            // For debugging VLC issues, you can enable verbose logging by using the following options:
            //_libVlc = new LibVLC("--no-video", "--verbose=2");
            //_libVlc.Log += LibVlc_Log;

            _libVlc = new LibVLC(
                        "--no-video",
                        "--quiet"
                    );

            Log.Information(
                "LibVLC instance created in {ElapsedMilliseconds} ms.",
                watch.ElapsedMilliseconds
            );

            _mediaPlayer = new MediaPlayer(_libVlc);

            _mediaPlayer.Playing += MediaPlayer_Playing;
            _mediaPlayer.Paused += MediaPlayer_Paused;
            _mediaPlayer.Stopped += MediaPlayer_Stopped;
            _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            _mediaPlayer.Buffering += MediaPlayer_Buffering;

            _isInitialized = true;

            watch.Stop();

            Log.Information(
                "VLC playback service initialized in {ElapsedMilliseconds} ms.",
                watch.ElapsedMilliseconds
            );
        }

        private void StopInternal()
        {
            try
            {
                if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Stop();
                }

                DisposeCurrentMedia();

                Log.Information("VLC playback stop requested.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to stop VLC playback safely.");
            }
        }

        private void DisposeCurrentMedia()
        {
            if (_currentMedia == null)
            {
                return;
            }

            try
            {
                _currentMedia.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to dispose current VLC media.");
            }
            finally
            {
                _currentMedia = null;
            }
        }

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            Log.Information(
                "VLC playback started. OriginalUrl: {OriginalUrl}, PlaybackUrl: {PlaybackUrl}",
                _currentOriginalUrl,
                _currentPlaybackUrl
            );

            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        private void MediaPlayer_Paused(object sender, EventArgs e)
        {
            Log.Information("VLC playback paused.");

            PlaybackPaused?.Invoke(this, EventArgs.Empty);
        }

        private void MediaPlayer_Stopped(object sender, EventArgs e)
        {
            Log.Information("VLC playback stopped.");

            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        private void MediaPlayer_EndReached(object sender, EventArgs e)
        {
            Log.Information("VLC playback ended.");

            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        private void MediaPlayer_Buffering(object sender, MediaPlayerBufferingEventArgs e)
        {
            Log.Debug(
                "VLC buffering. Cache: {CachePercent}. OriginalUrl: {OriginalUrl}, PlaybackUrl: {PlaybackUrl}",
                Math.Round(e.Cache),
                _currentOriginalUrl,
                _currentPlaybackUrl
            );
        }

        private void MediaPlayer_EncounteredError(object sender, EventArgs e)
        {
            string message = "LibVLC encountered a playback error.";

            Log.Error(
                "VLC playback error. OriginalUrl: {OriginalUrl}, PlaybackUrl: {PlaybackUrl}",
                _currentOriginalUrl,
                _currentPlaybackUrl
            );

            PlaybackFailed?.Invoke(this, message);
        }

        private void LibVlc_Log(object sender, LogEventArgs e)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
                              " [" + e.Level + "] " +
                              e.Module + ": " +
                              e.Message +
                              Environment.NewLine;

                File.AppendAllText(_vlcLogPath, line);
            }
            catch
            {
                // Ignore VLC log write errors.
            }
        }

        public void SetVolume(int volume)
        {
            if (volume < 0)
            {
                volume = 0;
            }

            if (volume > 100)
            {
                volume = 100;
            }

            if (_mediaPlayer == null)
            {
                return;
            }

            _mediaPlayer.Volume = volume;
        }
    }
}
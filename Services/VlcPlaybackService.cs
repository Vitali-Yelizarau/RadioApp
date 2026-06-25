using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RadioApp.Services
{
    public class VlcPlaybackService : IDisposable
    {
        private readonly object _syncRoot = new object();

        private readonly string _vlcLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "vlc.log");
        private LibVLC _libVlc;
        private MediaPlayer _mediaPlayer;
        private Media _currentMedia;

        private bool _isInitialized;
        private string _currentStreamUrl;
        private int _lastLoggedBufferingCache = -1;
        private int _lastRaisedBufferingCache = -1;

        // "Now playing" track title is polled periodically instead of relying solely on
        // Media.MetaChanged, because that event is not reliably re-raised by LibVLC when
        // the ICY StreamTitle changes mid-stream (it mainly fires once during initial parse).
        private Timer _nowPlayingPollTimer;
        private string _lastKnownTrackTitle;

        public event EventHandler PlaybackStarted;
        public event EventHandler PlaybackPaused;
        public event EventHandler PlaybackStopped;
        public event EventHandler<string> PlaybackFailed;
        public event EventHandler<string> NowPlayingTrackChanged;

        // Raised as VLC fills its network/live cache (0..100). Used by the UI to show
        // a "Buffering X%" indicator. De-duplicated by integer percent so the UI is not
        // flooded with identical values that VLC can report repeatedly.
        public event EventHandler<double> BufferingProgressChanged;

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

            await Task.Run(() =>
            {
                StartPlaybackInternal(streamUrl);
            });

            totalWatch.Stop();

            Log.Information(
                "VLC PlayAsync finished in {ElapsedMilliseconds} ms. StreamUrl: {StreamUrl}",
                totalWatch.ElapsedMilliseconds,
                streamUrl
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
                    StopNowPlayingPolling();

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
                    _currentStreamUrl = null;

                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to dispose VLC playback service safely.");
                }
            }
        }

        private void StartPlaybackInternal(string streamUrl)
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

                Thread.Sleep(500);

                // Reset buffering trackers so the new stream reports a fresh 0..100 cycle.
                _lastLoggedBufferingCache = -1;
                _lastRaisedBufferingCache = -1;

                _currentStreamUrl = streamUrl;

                Log.Information(
                    "VLC opening stream. StreamUrl: {StreamUrl}",
                    _currentStreamUrl
                );

                DisposeCurrentMedia();

                // VLC handles HTTP redirects natively, so no external URL resolution is needed
                _currentMedia = new Media(_libVlc, new Uri(_currentStreamUrl));

                _currentMedia.AddOption(":no-video");
                _currentMedia.AddOption(":network-caching=5000");
                _currentMedia.AddOption(":live-caching=8000");
                _currentMedia.AddOption(":http-reconnect=true");
                //_currentMedia.AddOption(":http-continuous");
                _currentMedia.AddOption(":http-user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");

                // Kept as a secondary signal: fires reliably on initial parse, occasionally
                // on later updates depending on stream/demuxer. The polling timer below is
                // the primary, reliable mechanism for live ICY "now playing" updates.
                _currentMedia.MetaChanged += CurrentMedia_MetaChanged;

                bool started = _mediaPlayer.Play(_currentMedia);

                if (!started)
                {
                    throw new InvalidOperationException("LibVLC could not start playback.");
                }

                StartNowPlayingPolling();

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
                StopNowPlayingPolling();

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
                _currentMedia.MetaChanged -= CurrentMedia_MetaChanged;
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

        private void StartNowPlayingPolling()
        {
            StopNowPlayingPolling();

            _lastKnownTrackTitle = null;

            _nowPlayingPollTimer = new Timer(
                callback: _ => PollNowPlayingTrack(),
                state: null,
                dueTime: TimeSpan.FromSeconds(3),
                period: TimeSpan.FromSeconds(5)
            );
        }

        private void StopNowPlayingPolling()
        {
            _nowPlayingPollTimer?.Dispose();
            _nowPlayingPollTimer = null;
        }

        private void PollNowPlayingTrack()
        {
            //Log.Warning("PollNowPlayingTrack tick fired."); // temporary diagnostic line

            Media media = _currentMedia;

            if (media == null)
            {
                Log.Warning("PollNowPlayingTrack: _currentMedia is null.");
                return;
            }

            string trackTitle;

            try
            {
                trackTitle = media.Meta(MetadataType.NowPlaying);
                //Log.Warning("PollNowPlayingTrack: raw value = {Value}", trackTitle);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read NowPlaying metadata during poll.");
                return;
            }

            if (string.IsNullOrWhiteSpace(trackTitle))
            {
                return;
            }

            if (string.Equals(trackTitle, _lastKnownTrackTitle, StringComparison.Ordinal))
            {
                return;
            }

            _lastKnownTrackTitle = trackTitle;

            Log.Information(
                "VLC now playing track changed (polled). Track: {Track}, StreamUrl: {StreamUrl}",
                trackTitle,
                _currentStreamUrl
            );

            NowPlayingTrackChanged?.Invoke(this, trackTitle);
        }

        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            Log.Information(
                "VLC playback started. StreamUrl: {StreamUrl}",
                _currentStreamUrl
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
            // Surface buffering progress to the UI, de-duplicated by integer percent
            // so identical repeated values don't flood the dispatcher.
            int cache = (int)Math.Round(e.Cache);

            if (cache != _lastRaisedBufferingCache)
            {
                _lastRaisedBufferingCache = cache;
                BufferingProgressChanged?.Invoke(this, e.Cache);
            }

            //Log each 5 percents to reduce the length of the log file,
            //and also avoid logging the same value multiple times in a row
            //(since VLC can report the same cache value repeatedly during buffering).
            const int DIVISOR_VALUE__FOR_LOGGER = 5;

            if (cache % DIVISOR_VALUE__FOR_LOGGER != 0)
            {
                return;
            }

            if (cache == _lastLoggedBufferingCache)
            {
                return;
            }

            _lastLoggedBufferingCache = cache;

            Log.Debug(
                "VLC buffering. Cache: {CachePercent}. StreamUrl: {StreamUrl}",
                cache,
                _currentStreamUrl
            );
        }

        private void MediaPlayer_EncounteredError(object sender, EventArgs e)
        {
            string message = "LibVLC encountered a playback error.";

            Log.Error(
                "VLC playback error. StreamUrl: {StreamUrl}",
                _currentStreamUrl
            );

            PlaybackFailed?.Invoke(this, message);
        }

        private void CurrentMedia_MetaChanged(object sender, MediaMetaChangedEventArgs e)
        {
            string rawValue;

            try
            {
                rawValue = _currentMedia?.Meta(e.MetadataType);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read changed metadata. MetadataType: {MetadataType}", e.MetadataType);
                return;
            }

            Log.Debug(
                "VLC media meta changed (event). MetadataType: {MetadataType}, Value: {Value}, StreamUrl: {StreamUrl}",
                e.MetadataType,
                rawValue,
                _currentStreamUrl
            );

            if (e.MetadataType != MetadataType.NowPlaying)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return;
            }

            if (string.Equals(rawValue, _lastKnownTrackTitle, StringComparison.Ordinal))
            {
                return;
            }

            _lastKnownTrackTitle = rawValue;

            Log.Information(
                "VLC now playing track changed (event). Track: {Track}, StreamUrl: {StreamUrl}",
                rawValue,
                _currentStreamUrl
            );

            NowPlayingTrackChanged?.Invoke(this, rawValue);
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
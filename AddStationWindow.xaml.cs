using RadioApp.Models;
using RadioApp.Services;
using Serilog;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RadioApp
{
    public partial class AddStationWindow : Window
    {
        const int MIN_TIMEOUT = 3;
        const int MAX_TIMEOUT = 300;

        private readonly RadioStreamDiscoveryService _discoveryService;
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
                    throw new InvalidOperationException("Timeout must be at least 3 seconds.");
                }

                if (seconds > MAX_TIMEOUT)
                {
                    throw new InvalidOperationException("Timeout must not be greater than 120 seconds.");
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

            _mode = StationWindowMode.Add;

            _discoveryService = new RadioStreamDiscoveryService();
            _streamInfoService = new RadioStreamInfoService();

            DiscoveredStream = new DiscoveredRadioStream();

            ConfigureAddMode();
        }

        public AddStationWindow(MediaItem item)
        {
            InitializeComponent();

            _mode = StationWindowMode.Edit;
            _editingItem = item ?? throw new ArgumentNullException(nameof(item));

            _discoveryService = new RadioStreamDiscoveryService();
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

            // Для Update по текущей логике не проверяем, играет ли поток.
            // Пользователь может вручную изменить Stream URL и Description.

            DiscoveredStream = new DiscoveredRadioStream
            {
                PageUrl = PageUrl,
                StreamUrl = streamUrl,
                StationName = _editingItem.Title,
                Description = UserDescription,
                Genre = _editingItem.Genre
            };
        }

        private async Task<DiscoveredRadioStream> DetectStreamFromPageAsync(string pageUrl, CancellationToken cancellationToken)
        {
            Log.Information("Stream URL is empty. Trying to detect stream from page URL: {PageUrl}", pageUrl);

            DiscoveredRadioStream result = await _discoveryService.DiscoverAsync(pageUrl, cancellationToken);

            if (result == null || string.IsNullOrWhiteSpace(result.StreamUrl))
            {
                throw new InvalidOperationException("Stream URL could not be detected from this page.");
            }

            ApplyResultToForm(result);

            Log.Information(
                "Stream detected from page. PageUrl: {PageUrl}, StreamUrl: {StreamUrl}, StationName: {StationName}",
                result.PageUrl,
                result.StreamUrl,
                result.StationName
            );

            return result;
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
    }
}
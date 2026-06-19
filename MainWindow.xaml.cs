using RadioApp.Models;
using RadioApp.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data.Entity.Validation;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RadioApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly RadioDatabaseService _databaseService;
        private readonly VlcPlaybackService _playbackService = new VlcPlaybackService();

        private List<MediaItem> _playlist;
        private Point _dragStartPoint;

        private MediaItem _currentlyPlayingStation;
        private bool _isPlaying;
        private bool _isPaused;
        private bool _isChangingStation;
        private MediaItem SelectedStation
        {
            get
            {
                return StationsListBox.SelectedItem as MediaItem;
            }
        }

        private enum StationSortField
        {
            Name,
            PlayCount
        }

        private enum SortDirection
        {
            Ascending,
            Descending
        }

        public MainWindow()
        {
            InitializeComponent();

            _databaseService = new RadioDatabaseService();
            _playlist = new List<MediaItem>();

            Loaded += async (s, e) => await MainWindow_LoadedAsync(s, e);

            _playbackService.PlaybackStarted += PlaybackService_PlaybackStarted;
            _playbackService.PlaybackPaused += PlaybackService_PlaybackPaused;
            _playbackService.PlaybackStopped += PlaybackService_PlaybackStopped;
            _playbackService.PlaybackFailed += PlaybackService_PlaybackFailed;
        }

        private async Task MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
        {
            Log.Information("MainWindow_Loaded started.");

            try
            {
                IsEnabled = false;

                var playlist = await _databaseService.InitializeDatabaseAndGetEnabledMediaItemsAsync();

                _playlist = playlist;

                StationsListBox.ItemsSource = _playlist;

                if (_playlist.Count > 0)
                {
                    StationsListBox.SelectedIndex = 0;
                }
                else
                {
                    Log.Information("Playlist is empty.");
                }

                _ = _playbackService.InitializeAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during MainWindow_Loaded");

                MessageBox.Show(
                    "Error starting the application. Details are logged.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private async void SortStationsByNameAscendingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await SortStationsAsync(StationSortField.Name, SortDirection.Ascending);
        }

        private async void SortStationsByNameDescendingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await SortStationsAsync(StationSortField.Name, SortDirection.Descending);
        }

        private async void SortStationsByPlayCountAscendingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await SortStationsAsync(StationSortField.PlayCount, SortDirection.Ascending);
        }

        private async void SortStationsByPlayCountDescendingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await SortStationsAsync(StationSortField.PlayCount, SortDirection.Descending);
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new AddStationWindow
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
            {
                return;
            }

            try
            {
                var stream = window.DiscoveredStream;

                string title = !string.IsNullOrWhiteSpace(window.UserTitle)
                                ? window.UserTitle
                                : !string.IsNullOrWhiteSpace(stream.StationName)
                                    ? stream.StationName
                                    : "New radio station";

                string description = !string.IsNullOrWhiteSpace(window.UserDescription)
                    ? window.UserDescription
                    : stream.Description;

                MediaItem savedItem = _databaseService.AddOrUpdateRadioStation(
                    title,
                    description,
                    stream.PageUrl,
                    stream.StreamUrl,
                    stream.Genre
                );

                await ReloadPlaylistAsync();

                var selectedAfterSave = _playlist.FirstOrDefault(x => x.Id == savedItem.Id);

                if (selectedAfterSave != null)
                {
                    StationsListBox.SelectedItem = selectedAfterSave;
                    StationsListBox.ScrollIntoView(selectedAfterSave);
                }
            }
            catch (DbEntityValidationException ex)
            {
                foreach (var validationErrors in ex.EntityValidationErrors)
                {
                    foreach (var validationError in validationErrors.ValidationErrors)
                    {
                        Log.Error(
                            "Entity validation error. Property: {PropertyName}, Error: {ErrorMessage}",
                            validationError.PropertyName,
                            validationError.ErrorMessage
                        );
                    }
                }

                Log.Error(ex, "Failed to add/update station because entity validation failed.");

                MessageBox.Show(
                    "Could not save this station. Details were written to log.",
                    "Save failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to add/update station.");

                MessageBox.Show(
                    "Could not save this station. Details were written to log.",
                    "Save failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void DeleteButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            await DeleteSelectedStationWithConfirmationAsync();
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = SelectedStation;

            if (selected == null)
            {
                MessageBox.Show(
                    "Please select a station to edit.",
                    "No station selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                return;
            }

            var window = new AddStationWindow(selected)
            {
                Owner = this
            };

            bool? result = window.ShowDialog();

            if (result != true)
            {
                return;
            }

            try
            {
                MediaItem updatedItem = window.UpdatedItem;

                Log.Information(
                    "Updating station. Id: {Id}, Title: {Title}, StreamUrl: {StreamUrl}",
                    updatedItem.Id,
                    updatedItem.Title,
                    updatedItem.StreamUrl
                );

                _databaseService.UpdateRadioStation(updatedItem);

                int oldIndex = StationsListBox.SelectedIndex;

                await ReloadPlaylistAsync();

                if (_playlist.Count > 0)
                {
                    if (oldIndex < 0)
                    {
                        oldIndex = 0;
                    }

                    if (oldIndex >= _playlist.Count)
                    {
                        oldIndex = _playlist.Count - 1;
                    }

                    StationsListBox.SelectedIndex = oldIndex;
                    StationsListBox.ScrollIntoView(_playlist[oldIndex]);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update station. Id: {Id}", selected.Id);

                MessageBox.Show(
                    "Could not update this station. Details were written to log.",
                    "Update failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async void StationsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            MediaItem selected = SelectedStation;

            if (selected == null)
            {
                return;
            }

            await RunPlaybackOperationAsync(async () =>
            {
                await PlayStationAsync(selected);
            });
        }

        private void StationsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void StationsListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point currentPosition = e.GetPosition(null);

            if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            MediaItem draggedItem = GetMediaItemFromMousePosition(e.GetPosition(StationsListBox));

            if (draggedItem == null)
            {
                return;
            }

            DragDrop.DoDragDrop(
                StationsListBox,
                draggedItem,
                DragDropEffects.Move
            );
        }

        private async void StationsListBox_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(MediaItem)))
            {
                return;
            }

            MediaItem droppedItem = e.Data.GetData(typeof(MediaItem)) as MediaItem;

            if (droppedItem == null)
            {
                return;
            }

            MediaItem targetItem = GetMediaItemFromMousePosition(e.GetPosition(StationsListBox));

            if (targetItem == null)
            {
                return;
            }

            if (droppedItem.Id == targetItem.Id)
            {
                return;
            }

            int oldIndex = _playlist.IndexOf(droppedItem);
            int newIndex = _playlist.IndexOf(targetItem);

            if (oldIndex < 0 || newIndex < 0)
            {
                return;
            }

            _playlist.RemoveAt(oldIndex);
            _playlist.Insert(newIndex, droppedItem);

            await SavePlaylistOrderAsync();

            StationsListBox.ItemsSource = null;
            StationsListBox.ItemsSource = _playlist;
            StationsListBox.SelectedItem = droppedItem;
            StationsListBox.ScrollIntoView(droppedItem);
        }

        private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            MediaItem selected = SelectedStation;

            if (selected == null)
            {
                Log.Information("Play/Pause button clicked, but no station is selected.");
                return;
            }

            if (_currentlyPlayingStation != null &&
                _currentlyPlayingStation.Id == selected.Id)
            {
                if (_isPlaying)
                {
                    PauseCurrentStation();
                    return;
                }

                if (_isPaused)
                {
                    ResumeCurrentStation();
                    return;
                }
            }

            await RunPlaybackOperationAsync(async () =>
            {
                await PlayStationAsync(selected);
            });
        }

        private async void NextItemButton_Click(object sender, RoutedEventArgs e)
        {
            await SwitchStationAsync(1);
        }

        private async void PreviousItemButton_Click(object sender, RoutedEventArgs e)
        {
            await SwitchStationAsync(-1);
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int volume = (int)e.NewValue;

            if (VolumeValueTextBlock != null)
            {
                VolumeValueTextBlock.Text = volume + "%";
            }

            _playbackService.SetVolume(volume);
        }

        private void PlaybackService_PlaybackStarted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = true;
                _isPaused = false;

                UpdatePlaybackUi();
            });
        }

        private void PlaybackService_PlaybackPaused(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                _isPaused = true;

                UpdatePlaybackUi();
            });
        }

        private void PlaybackService_PlaybackStopped(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                _isPaused = false;

                UpdatePlaybackUi();
            });
        }

        private void PlaybackService_PlaybackFailed(object sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                _isPlaying = false;
                _isPaused = false;
                _currentlyPlayingStation = null;

                UpdatePlaybackUi();

                MessageBox.Show(
                    message,
                    "Playback failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _playbackService.PlaybackStarted -= PlaybackService_PlaybackStarted;
                _playbackService.PlaybackPaused -= PlaybackService_PlaybackPaused;
                _playbackService.PlaybackStopped -= PlaybackService_PlaybackStopped;
                _playbackService.PlaybackFailed -= PlaybackService_PlaybackFailed;

                StopCurrentStation();

                _playbackService.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error while closing VLC playback service.");
            }

            base.OnClosed(e);
        }

        private void SetPlaybackOperationUiBusy(bool isBusy)
        {
            _isChangingStation = isBusy;

            PlayPauseButton.IsEnabled = !isBusy;
            NextItemButton.IsEnabled = !isBusy;
            PreviousItemButton.IsEnabled = !isBusy;
            AddButton.IsEnabled = !isBusy;
            EditButton.IsEnabled = !isBusy;
            DeleteButton.IsEnabled = !isBusy;
            StationsListBox.IsEnabled = !isBusy;

            Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;
        }

        private async Task RunPlaybackOperationAsync(Func<Task> operation)
        {
            if (_isChangingStation)
            {
                return;
            }

            try
            {
                SetPlaybackOperationUiBusy(true);

                await operation();
            }
            finally
            {
                SetPlaybackOperationUiBusy(false);
            }
        }

        private async Task PlayStationAsync(MediaItem station)
        {
            if (station == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(station.StreamUrl))
            {
                MessageBox.Show(
                    "Selected station does not have Stream URL.",
                    "Cannot play station",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );

                return;
            }

            try
            {
                Log.Information(
                    "Starting playback. StationId: {StationId}, Title: {Title}, StreamUrl: {StreamUrl}",
                    station.Id,
                    station.Title,
                    station.StreamUrl
                );

                await _playbackService.PlayAsync(station.StreamUrl);

                _playbackService.SetVolume((int)VolumeSlider.Value);

                await _databaseService.IncrementPlayCountAsync(station.Id);
                station.PlayCount++;

                _currentlyPlayingStation = station;
                _isPlaying = true;
                _isPaused = false;

                UpdatePlaybackUi();
            }
            catch (Exception ex)
            {
                _isPlaying = false;
                _isPaused = false;
                _currentlyPlayingStation = null;

                UpdatePlaybackUi();

                Log.Error(
                    ex,
                    "Failed to start playback. StationId: {StationId}, Title: {Title}, StreamUrl: {StreamUrl}",
                    station.Id,
                    station.Title,
                    station.StreamUrl
                );

                MessageBox.Show(
                    "Could not play this station. Details were written to log.",
                    "Playback failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void PauseCurrentStation()
        {
            if (!_isPlaying || _currentlyPlayingStation == null)
            {
                return;
            }

            try
            {
                _playbackService.Pause();

                _isPlaying = false;
                _isPaused = true;

                Log.Information(
                    "Playback paused. StationId: {StationId}, Title: {Title}",
                    _currentlyPlayingStation.Id,
                    _currentlyPlayingStation.Title
                );

                UpdatePlaybackUi();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to pause playback.");
            }
        }

        private void ResumeCurrentStation()
        {
            if (!_isPaused || _currentlyPlayingStation == null)
            {
                return;
            }

            try
            {
                _playbackService.Resume();

                _isPlaying = true;
                _isPaused = false;

                Log.Information(
                    "Playback resumed. StationId: {StationId}, Title: {Title}",
                    _currentlyPlayingStation.Id,
                    _currentlyPlayingStation.Title
                );

                UpdatePlaybackUi();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to resume playback.");
            }
        }

        private void StopCurrentStation()
        {
            try
            {
                _playbackService.Stop();

                _isPlaying = false;
                _isPaused = false;
                _currentlyPlayingStation = null;

                Log.Information("Playback stopped.");

                UpdatePlaybackUi();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to stop current station.");
            }
        }

        /// <summary>
        /// Shared deletion logic for the Delete button and the right-click context menu.
        /// Asks the user to confirm, deletes the selected station, and reloads the playlist.
        /// </summary>
        private async Task DeleteSelectedStationWithConfirmationAsync()
        {
            MediaItem selected = SelectedStation;

            if (selected == null)
            {
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                $"Delete \"{selected.Title}\"?",
                "Confirm delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (choice != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _databaseService.DeleteMediaItem(selected.Id);

                Log.Information(
                    "Station deleted. Id: {Id}, Title: {Title}",
                    selected.Id,
                    selected.Title
                );

                _playlist = await _databaseService.GetEnabledMediaItems();
                StationsListBox.ItemsSource = _playlist;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to delete station. Id: {Id}", selected.Id);

                MessageBox.Show(
                    "Could not delete this station. Details were written to log.",
                    "Delete failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private async Task ReloadPlaylistAsync()
        {
            _playlist = await _databaseService.InitializeDatabaseAndGetEnabledMediaItemsAsync();

            StationsListBox.ItemsSource = null;
            StationsListBox.ItemsSource = _playlist;

            Log.Information("Playlist reloaded. Items count: {Count}", _playlist.Count);
        }

        private void UpdatePlaybackUi()
        {
            if (_currentlyPlayingStation == null)
            {
                PlayPauseButton.Content = "▶";
                NowPlayingTextBlock.Text = "Now playing:";
                return;
            }

            if (_isPlaying)
            {
                PlayPauseButton.Content = "⏸";
                NowPlayingTextBlock.Text = "Now playing: " + _currentlyPlayingStation.Title;
                return;
            }

            if (_isPaused)
            {
                PlayPauseButton.Content = "▶";
                NowPlayingTextBlock.Text = "Paused: " + _currentlyPlayingStation.Title;
                return;
            }

            PlayPauseButton.Content = "▶";
            NowPlayingTextBlock.Text = "Now playing:";
        }

        private async Task SwitchStationAsync(int direction)
        {
            int stationCount = StationsListBox.Items.Count;

            if (stationCount == 0)
            {
                return;
            }

            int currentIndex = StationsListBox.SelectedIndex;
            int newIndex;

            if (currentIndex < 0)
            {
                newIndex = direction > 0 ? 0 : stationCount - 1;
            }
            else
            {
                newIndex = currentIndex + direction;

                if (newIndex >= stationCount)
                {
                    newIndex = 0;
                }
                else if (newIndex < 0)
                {
                    newIndex = stationCount - 1;
                }
            }

            StationsListBox.SelectedIndex = newIndex;
            StationsListBox.ScrollIntoView(StationsListBox.SelectedItem);

            MediaItem selected = SelectedStation;

            if (selected == null)
            {
                return;
            }

            await RunPlaybackOperationAsync(async () =>
            {
                await PlayStationAsync(selected);
            });
        }

        private MediaItem GetMediaItemFromMousePosition(Point point)
        {
            DependencyObject element = StationsListBox.InputHitTest(point) as DependencyObject;

            while (element != null)
            {
                ListBoxItem listBoxItem = element as ListBoxItem;

                if (listBoxItem != null)
                {
                    return listBoxItem.DataContext as MediaItem;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return null;
        }

        private async Task SavePlaylistOrderAsync()
        {
            for (int i = 0; i < _playlist.Count; i++)
            {
                _playlist[i].SortOrder = i;
            }

            await _databaseService.UpdateSortOrderAsync(_playlist);

            Log.Information(
                "Playlist order saved. Items count: {Count}",
                _playlist.Count
            );
        }

        private async Task SortStationsAsync(StationSortField field, SortDirection direction)
        {
            if (_playlist == null || _playlist.Count == 0)
            {
                return;
            }

            MediaItem selectedItem = StationsListBox.SelectedItem as MediaItem;

            switch (field)
            {
                case StationSortField.Name:
                    _playlist = direction == SortDirection.Ascending
                        ? _playlist
                            .OrderBy(x => x.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            .ToList()
                        : _playlist
                            .OrderByDescending(x => x.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            .ToList();
                    break;

                case StationSortField.PlayCount:
                    _playlist = direction == SortDirection.Ascending
                        ? _playlist
                            .OrderBy(x => x.PlayCount)
                            .ThenBy(x => x.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            .ToList()
                        : _playlist
                            .OrderByDescending(x => x.PlayCount)
                            .ThenBy(x => x.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                            .ToList();
                    break;

                default:
                    throw new InvalidOperationException("Unsupported station sort field: " + field);
            }

            await ApplyPlaylistOrderAsync(selectedItem);

            Log.Information(
                "Playlist sorted. Field: {Field}, Direction: {Direction}, Items count: {Count}",
                field,
                direction,
                _playlist.Count
            );
        }

        private async Task ApplyPlaylistOrderAsync(MediaItem selectedItem)
        {
            for (int i = 0; i < _playlist.Count; i++)
            {
                _playlist[i].SortOrder = i;
            }

            await _databaseService.UpdateSortOrderAsync(_playlist);

            StationsListBox.ItemsSource = null;
            StationsListBox.ItemsSource = _playlist;

            if (selectedItem == null)
            {
                return;
            }

            MediaItem selectedAfterSort = _playlist.FirstOrDefault(x => x.Id == selectedItem.Id);

            if (selectedAfterSort != null)
            {
                StationsListBox.SelectedItem = selectedAfterSort;
                StationsListBox.ScrollIntoView(selectedAfterSort);
            }
        }

        private void StationsListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Select the item under the right-click so context menu actions target it
            MediaItem clickedItem = GetMediaItemFromMousePosition(e.GetPosition(StationsListBox));

            if (clickedItem != null)
            {
                StationsListBox.SelectedItem = clickedItem;
            }
        }

        private async void ContextPlayMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MediaItem selected = SelectedStation;

            if (selected == null)
                return;

            await RunPlaybackOperationAsync(async () =>
            {
                await PlayStationAsync(selected);
            });
        }

        private void ContextEditMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Reuse the same logic as the Edit button
            EditButton_Click(sender, e);
        }

        private async void ContextDeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await DeleteSelectedStationWithConfirmationAsync();
        }
    }
}
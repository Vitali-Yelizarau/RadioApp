using RadioApp.Data;
using RadioApp.Models;
using RadioApp.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace RadioApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly RadioDatabaseService _databaseService;

        private List<MediaItem> _playlist;
        private int _currentIndex;
        private bool _isPlaying;
        private MediaItem SelectedStation
        {
            get
            {
                return StationsListBox.SelectedItem as MediaItem;
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            _databaseService = new RadioDatabaseService();
            _playlist = new List<MediaItem>();

            Loaded += async (s, e) => await MainWindow_LoadedAsync(s, e);
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
                //StationsListBox.DisplayMemberPath = "Title";

                if (_playlist.Count > 0)
                {
                    _currentIndex = 0;
                    StationsListBox.SelectedIndex = 0;
                }
                else
                {
                    _currentIndex = -1;
                    Log.Information("Playlist is empty.");
                }
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

        public void DeleteRadioStation(int mediaItemId)
        {
            using (var db = new RadioDbContext())
            {
                var item = db.MediaItems.FirstOrDefault(x => x.Id == mediaItemId);

                if (item == null)
                {
                    return;
                }

                db.MediaItems.Remove(item);
                db.SaveChanges();
            }
        }

        private async void DeleteButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            if (StationsListBox.SelectedItem is MediaItem selectedItem)
            {
                DeleteRadioStation(selectedItem.Id);
                _playlist = await _databaseService.GetEnabledMediaItems();
                StationsListBox.ItemsSource = _playlist;
            }
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
        private async Task ReloadPlaylistAsync()
        {
            _playlist = await _databaseService.InitializeDatabaseAndGetEnabledMediaItemsAsync();

            StationsListBox.ItemsSource = null;
            StationsListBox.ItemsSource = _playlist;
            //StationsListBox.DisplayMemberPath = "Title";

            Log.Information("Playlist reloaded. Items count: {Count}", _playlist.Count);
        }
    }
}

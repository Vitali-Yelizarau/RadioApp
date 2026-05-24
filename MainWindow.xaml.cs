using RadioApp.Models;
using RadioApp.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

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
                StationsListBox.DisplayMemberPath = "Title";

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
    }
}

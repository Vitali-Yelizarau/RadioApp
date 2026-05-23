using RadioApp.Models;
using RadioApp.Services;
using System;
using System.Collections.Generic;
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

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _databaseService.InitializeDatabase();

            //MessageBox.Show("Database initialized.");

            _playlist = _databaseService.GetEnabledMediaItems();

            StationsListBox.ItemsSource = _playlist;
            StationsListBox.DisplayMemberPath = "Title";

            if (_playlist.Count > 0)
            {
                _currentIndex = 0;
                StationsListBox.SelectedIndex = 0;

                // NowPlayingTextBlock.Text = "Selected: " + _playlist[0].Title;
            }
            else
            {
                // NowPlayingTextBlock.Text = "Playlist is empty.";
            }
        }
    }
}

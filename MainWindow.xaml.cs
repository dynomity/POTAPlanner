using Microsoft.Win32;
using POTAPlanner.Models;
using POTAPlanner.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;

namespace POTAPlanner;

public partial class MainWindow : Window
{
    private readonly CsvService _csvService = new();

    private readonly ObservableCollection<Park> _parks = new();

    private readonly CollectionViewSource _viewSource = new();

    public MainWindow()
    {
        InitializeComponent();

        _viewSource.Source = _parks;
        ParksGrid.ItemsSource = _viewSource.View;

        StatusText.Text = "No parks loaded";
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open POTA CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var parks = _csvService.LoadParks(dialog.FileName);

            _parks.Clear();

            foreach (var park in parks.OrderBy(p => p.Reference))
                _parks.Add(park);

            StatusText.Text = $"{_parks.Count:N0} parks loaded";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Unable to load CSV",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        string search = SearchBox.Text.Trim().ToLower();

        _viewSource.View.Filter = obj =>
        {
            if (obj is not Park park)
                return false;

            if (string.IsNullOrWhiteSpace(search))
                return true;

            return park.Reference.ToLower().Contains(search)
                || park.Name.ToLower().Contains(search)
                || park.Grid.ToLower().Contains(search);
        };

        _viewSource.View.Refresh();

        StatusText.Text = $"{_viewSource.View.Cast<object>().Count():N0} parks shown";
    }

    private void ParksGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ParksGrid.SelectedItem is not Park park)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = park.GoogleMapsUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                "Unable to open Google Maps.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
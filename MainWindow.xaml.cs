using Mapsui;
using Mapsui.Features;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Tiling;
using Microsoft.Win32;
using NetTopologySuite.Geometries;
using POTAPlanner.Models;
using POTAPlanner.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace POTAPlanner;

internal enum FilterMode { All, NeverActivated, Rare }
internal sealed record ProvinceOption(string Code, string Name)
{
    public override string ToString() => $"{Name} ({Code})";
}

public partial class MainWindow : Window
{
    private readonly CsvService _csvService = new();
    private readonly ExcelService _excelService = new();
    private readonly RoutingService _routingService = new();
    private readonly ParkDataService _parkDataService = new();
    private readonly PotaApiService _potaApiService = new();
    private readonly ObservableCollection<Park> _parks = new();
    private readonly ObservableCollection<RouteStop> _routeStops = new();
    private readonly ObservableCollection<ProvinceOption> _provinceOptions = new();
    private readonly CollectionViewSource _viewSource = new();
    private readonly Map _map = new();
    private readonly MemoryLayer _parksLayer = new("Parks") { Style = null };
    private readonly MemoryLayer _routeLayer = new("Planned Route") { Style = null };
    private readonly MemoryLayer _selectedParkLayer = new("Selected Park") { Style = null };
    private readonly HashSet<string> _plannedReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedProvinceCodes = new(StringComparer.OrdinalIgnoreCase);
    private FilterMode _filterMode = FilterMode.All;
    private bool _routePlanningMode;
    private bool _hideNonRouteParks;
    private Park? _selectedPark;
    private RoutePlan? _activeRoute;

    public MainWindow()
    {
        InitializeComponent();

        _viewSource.Source = _parks;
        ParksGrid.ItemsSource = _viewSource.View;
        RouteStopsGrid.ItemsSource = _routeStops;
        ProvinceListBox.ItemsSource = _provinceOptions;

        _map.Layers.Add(OpenStreetMap.CreateTileLayer());
        _map.Layers.Add(_routeLayer);
        _map.Layers.Add(_parksLayer);
        _map.Layers.Add(_selectedParkLayer);
        ParkMap.Map = _map;
        ParkMap.Info += ParkMap_Info;

        StatusText.Text = "No parks loaded";
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e) => LoadBundledParks();

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
            LoadParksFromFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to load CSV", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateDataButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Update Parks Data from POTA CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var importedParks = _csvService.LoadParks(dialog.FileName);
            if (importedParks.Count == 0)
                throw new InvalidOperationException("The selected CSV does not contain any parks.");

            _parkDataService.UpdateFrom(dialog.FileName);
            LoadParksFromFile(_parkDataService.GetPreferredDataPath());

            MessageBox.Show(
                $"Updated the local Canada dataset with {importedParks.Count:N0} parks.",
                "Parks Data Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Update Parks Data", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void UpdateFromPotaButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateFromPotaButton.IsEnabled = false;
        UpdateFromPotaButton.Content = "Updating…";

        try
        {
            var parks = await _potaApiService.DownloadCanadaParksAsync();
            _parkDataService.UpdateFromParks(parks, _csvService);
            LoadParksFromFile(_parkDataService.GetPreferredDataPath());

            MessageBox.Show(
                $"Updated the local Canada dataset with {parks.Count:N0} parks from POTA.",
                "Parks Data Updated",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not update from POTA. You can still use Update from CSV.\n\n{ex.Message}",
                "Unable to Update from POTA",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateFromPotaButton.IsEnabled = true;
            UpdateFromPotaButton.Content = "Update from POTA";
        }
    }

    private void LoadBundledParks()
    {
        try
        {
            LoadParksFromFile(_parkDataService.GetPreferredDataPath());
        }
        catch (Exception ex)
        {
            StatusText.Text = "Canada parks data could not be loaded";
            MessageBox.Show(ex.Message, "Unable to Load Parks Data", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadParksFromFile(string filePath)
    {
        var parks = _csvService.LoadParks(filePath);
        _parks.Clear();

        foreach (var park in parks.OrderBy(park => park.Reference))
            _parks.Add(park);

        PopulateProvinceSelector();
        ClearRoute();
        ApplyFilter();
        ParksGrid.SelectedItem = null;
        SetSelectedPark(null, zoomToPark: false);
        RebuildParkMarkers();
        ZoomToAllParks();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        string search = SearchBox.Text.Trim().ToLowerInvariant();

        _viewSource.View.Filter = item =>
        {
            if (item is not Park park)
                return false;

            bool matchesFilter = _filterMode switch
            {
                FilterMode.NeverActivated => park.Activations == 0,
                FilterMode.Rare => park.Activations <= 5,
                _ => true
            };

            bool matchesSearch = string.IsNullOrWhiteSpace(search)
                || park.Reference.Contains(search, StringComparison.OrdinalIgnoreCase)
                || park.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || park.Grid.Contains(search, StringComparison.OrdinalIgnoreCase);

            bool isPlannedStop = !_routePlanningMode || _plannedReferences.Contains(park.Reference);
            return matchesFilter && matchesSearch && isPlannedStop && MatchesSelectedProvinces(park);
        };

        _viewSource.View.Refresh();
        RebuildParkMarkers();
        string mode = _routePlanningMode ? " planned stops" : " parks";
        StatusText.Text = $"Showing {_viewSource.View.Cast<object>().Count():N0}{mode} of {_parks.Count:N0} parks";
    }

    private void AllButton_Click(object sender, RoutedEventArgs e)
    {
        ClearParkSelection();
        NeverButton.IsChecked = false;
        RareButton.IsChecked = false;
        _filterMode = FilterMode.All;
        ApplyFilter();
    }

    private void NeverButton_Click(object sender, RoutedEventArgs e)
    {
        ClearParkSelection();
        if (NeverButton.IsChecked == true)
        {
            RareButton.IsChecked = false;
            _filterMode = FilterMode.NeverActivated;
        }
        else
        {
            _filterMode = FilterMode.All;
        }

        ApplyFilter();
    }

    private void RareButton_Click(object sender, RoutedEventArgs e)
    {
        ClearParkSelection();
        if (RareButton.IsChecked == true)
        {
            NeverButton.IsChecked = false;
            _filterMode = FilterMode.Rare;
        }
        else
        {
            _filterMode = FilterMode.All;
        }

        ApplyFilter();
    }

    private void ProvinceButton_Click(object sender, RoutedEventArgs e)
    {
        ClearParkSelection();
        ProvincePopup.IsOpen = !ProvincePopup.IsOpen;
    }

    private void AllProvincesButton_Click(object sender, RoutedEventArgs e)
    {
        ClearParkSelection();
        ProvinceListBox.SelectedItems.Clear();
        ProvincePopup.IsOpen = false;
    }

    private void ProvinceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClearParkSelection();
        _selectedProvinceCodes.Clear();
        foreach (ProvinceOption province in ProvinceListBox.SelectedItems)
            _selectedProvinceCodes.Add(province.Code);

        UpdateProvinceButtonText();
        ApplyFilter();
    }

    private void ZoomAllButton_Click(object sender, RoutedEventArgs e)
    {
        ClearParkSelection();
        ZoomToAllParks();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        ClearParkSelection();
        var assembly = Assembly.GetExecutingAssembly();
        string version = assembly.GetName().Version?.ToString(3) ?? "Unknown";
        string compileDate = File.GetLastWriteTime(assembly.Location).ToString("yyyy-MM-dd HH:mm");

        MessageBox.Show(
            $"POTA Planner by VA6DM\n\n" +
            $"Version: {version}\n" +
            $"Compiled: {compileDate}\n\n" +
            "Created by: VA6DM\n" +
            "Email: va6dm@dmnet.ca\n\n" +
            "Copyright © 2026 VA6DM\n" +
            "Licensed under the GNU General Public License v3.0 or later.\n\n" +
            $"Parks data: {_parkDataService.GetDataSourceDescription()}\n" +
            "Map data: OpenStreetMap\n" +
            "Park data: Parks on the Air",
            "About POTA Planner by VA6DM",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void PlanRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_parks.Count == 0)
        {
            MessageBox.Show("Load a POTA CSV before planning a route.", "Plan Route", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!double.TryParse(RouteCorridorBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double corridorKm)
            && !double.TryParse(RouteCorridorBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out corridorKm))
        {
            MessageBox.Show("Enter a valid route-corridor distance in kilometres.", "Plan Route", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PlanRouteButton.IsEnabled = false;
        PlanRouteButton.Content = "Planning…";

        try
        {
            var route = await _routingService.PlanRouteAsync(
                RouteStartBox.Text,
                RouteDestinationBox.Text,
                corridorKm);

            var stops = _routingService.FindStopsAlongRoute(route, _parks);
            _activeRoute = route;
            SetRouteStops(stops);
            _plannedReferences.Clear();
            foreach (var stop in stops)
                _plannedReferences.Add(stop.Reference);
            _routePlanningMode = true;
            RebuildParkMarkers();
            ApplyFilter();
            UpdateParkDetails(_selectedPark);
            DrawRoute(route);

            StatusText.Text = $"Route: {route.DistanceKm:N0} km, {route.DurationMinutes / 60d:N1} hours; {stops.Count:N0} parks within {corridorKm:N0} km of the route";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Plan Route", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            PlanRouteButton.IsEnabled = true;
            PlanRouteButton.Content = "Plan Route";
        }
    }

    private void ClearRouteButton_Click(object sender, RoutedEventArgs e) => ClearRoute();

    private void HideNonRouteParksToggle_Changed(object sender, RoutedEventArgs e)
    {
        _hideNonRouteParks = HideNonRouteParksToggle.IsChecked == true;
        if (_hideNonRouteParks)
            ClearParkSelection();

        RebuildParkMarkers();
    }

    private void ClearRoute()
    {
        _routePlanningMode = false;
        _hideNonRouteParks = false;
        HideNonRouteParksToggle.IsChecked = false;
        _activeRoute = null;
        _plannedReferences.Clear();
        _routeLayer.Features = Array.Empty<IFeature>();
        _routeStops.Clear();
        UpdateRouteStopsHeader();
        RebuildParkMarkers();
        ApplyFilter();
        UpdateParkDetails(_selectedPark);
        ZoomToAllParks();
    }

    private void RouteStopsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RouteStopsGrid.SelectedItem is not RouteStop stop)
            return;

        SelectPark(stop.Park);
    }

    private void ParksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SetSelectedPark(ParksGrid.SelectedItem as Park);
    }

    private void ParksGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selectedPark is Park park)
            OpenGoogleMaps(park);
    }

    private void GoogleMapsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPark is Park park)
            OpenGoogleMaps(park);
    }

    private void AddToRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPark is Park park)
            AddParkToRoute(park);
    }

    private void ParkMap_Info(object? sender, MapInfoEventArgs e)
    {
        var mapInfo = e.GetMapInfo(new[] { _parksLayer });
        if (mapInfo?.Feature? ["Reference"] is not string reference)
            return;

        var park = _parks.FirstOrDefault(candidate => candidate.Reference == reference);
        if (park is null)
            return;

        SelectPark(park);
        e.Handled = true;
    }

    private void SelectPark(Park park)
    {
        bool visibleInGrid = _viewSource.View.Cast<Park>().Any(candidate => candidate.Reference == park.Reference);
        if (visibleInGrid)
        {
            ParksGrid.SelectedItem = park;
            ParksGrid.ScrollIntoView(park);
        }
        else
        {
            ParksGrid.SelectedItem = null;
            SetSelectedPark(park);
        }

        if (_routePlanningMode && _activeRoute is not null && !_plannedReferences.Contains(park.Reference))
        {
            var stop = _routingService.CreateRouteStop(_activeRoute, park);
            var result = MessageBox.Show(
                $"{park.Reference} — {park.Name} is not in the planned route.\n\n" +
                $"It is {stop.DistanceFromRouteKm:N1} km from the route. Add it anyway?",
                "Add Park to Route",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                AddParkToRoute(park);
        }
    }

    private void AddParkToRoute(Park park)
    {
        if (_activeRoute is null || _plannedReferences.Contains(park.Reference))
            return;

        var stop = _routingService.CreateRouteStop(_activeRoute, park);
        _plannedReferences.Add(stop.Reference);
        SetRouteStops(_routeStops.Concat(new[] { stop }).ToList());
        RebuildParkMarkers();
        ApplyFilter();

        ClearParkSelection();
        DrawRoute(_activeRoute);
        StatusText.Text = $"Added {park.Reference} to the planned route ({stop.DistanceFromRouteKm:N1} km from the route).";
    }

    private void RebuildParkMarkers()
    {
        var features = new List<IFeature>();

        foreach (var park in _parks)
        {
            if (!_routePlanningMode && !MatchesSelectedProvinces(park))
                continue;

            if (_routePlanningMode && _hideNonRouteParks && !_plannedReferences.Contains(park.Reference))
                continue;

            var position = ToMapPoint(park);
            var feature = new PointFeature(position)
            {
                ["Reference"] = park.Reference
            };

            feature.Styles.Add(CreateParkMarkerStyle(GetParkMarkerColor(park)));
            features.Add(feature);
        }

        _parksLayer.Features = features;
        _map.Refresh();
    }

    private void UpdateSelectedMarker(Park? park)
    {
        if (park is null || (_routePlanningMode && _hideNonRouteParks && !_plannedReferences.Contains(park.Reference)))
        {
            _selectedParkLayer.Features = Array.Empty<IFeature>();
        }
        else
        {
            var position = ToMapPoint(park);
            var feature = new PointFeature(position);
            feature.Styles.Add(CreateSelectedParkMarkerStyle());
            _selectedParkLayer.Features = new[] { feature };
        }

        _map.Refresh();
    }

    private void DrawRoute(RoutePlan route)
    {
        var geometryFactory = new GeometryFactory(new PrecisionModel(), 3857);
        var coordinates = route.Points
            .Select(point => ToMapPoint(point))
            .Select(point => new Coordinate(point.X, point.Y))
            .ToArray();

        var feature = new GeometryFeature(geometryFactory.CreateLineString(coordinates));
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(Color.ForestGreen, 4)
        });

        _routeLayer.Features = new[] { feature };
        _map.Refresh();

        var extent = feature.Extent;
        if (extent is not null)
            _map.Navigator.ZoomToBox(extent, MBoxFit.Fit, 300);
    }

    private Color GetParkMarkerColor(Park park)
    {
        if (_routePlanningMode && _plannedReferences.Contains(park.Reference))
            return Color.Blue;

        if (_filterMode == FilterMode.NeverActivated
            && park.Activations == 0
            && MatchesSelectedProvinces(park))
            return Color.ForestGreen;

        if (_filterMode == FilterMode.Rare
            && park.Activations <= 5
            && MatchesSelectedProvinces(park))
            return Color.Gold;

        return _routePlanningMode ? Color.Gray : Color.Blue;
    }

    private static SymbolStyle CreateParkMarkerStyle(Color color) => new()
    {
        SymbolScale = 0.7,
        Fill = new Brush(color),
        Outline = new Pen(Color.White, 1)
    };

    private static SymbolStyle CreateSelectedParkMarkerStyle() => new()
    {
        SymbolScale = 1.25,
        Fill = new Brush(Color.Red),
        Outline = new Pen(Color.White, 2)
    };

    private void ZoomToPark(Park park)
    {
        var position = ToMapPoint(park);
        _map.Navigator.CenterOnAndZoomTo(position, 150, 300);
    }

    private void ZoomToAllParks()
    {
        if (_parks.Count == 0)
            return;

        var zoomParks = _routePlanningMode && _routeStops.Count > 0
            ? _routeStops.Select(stop => stop.Park)
            : _parks.Where(MatchesSelectedProvinces);

        var points = zoomParks
            .Select(ToMapPoint)
            .ToList();

        if (points.Count == 0)
            return;

        double minX = points.Min(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxX = points.Max(point => point.X);
        double maxY = points.Max(point => point.Y);

        if (minX == maxX && minY == maxY)
        {
            _map.Navigator.CenterOnAndZoomTo(points[0], 150, 300);
            return;
        }

        double paddingX = Math.Max((maxX - minX) * 0.08, 5_000);
        double paddingY = Math.Max((maxY - minY) * 0.08, 5_000);
        var extent = new MRect(minX - paddingX, minY - paddingY, maxX + paddingX, maxY + paddingY);
        _map.Navigator.ZoomToBox(extent, MBoxFit.Fit, 300);
    }

    private static MPoint ToMapPoint(Park park)
        => ToMapPoint(new RoutePoint(park.Latitude, park.Longitude));

    private static MPoint ToMapPoint(RoutePoint point)
    {
        const double earthRadius = 6_378_137;
        double latitude = Math.Clamp(point.Latitude, -85.05112878, 85.05112878);
        double x = earthRadius * point.Longitude * Math.PI / 180d;
        double y = earthRadius * Math.Log(Math.Tan(Math.PI / 4d + latitude * Math.PI / 360d));
        return new MPoint(x, y);
    }

    private void UpdateParkDetails(Park? park)
    {
        ReferenceDetail.Text = park?.Reference ?? "—";
        NameDetail.Text = park?.Name ?? "—";
        GridDetail.Text = park?.Grid ?? "—";
        ActivationsDetail.Text = park?.Activations.ToString("N0") ?? "—";
        QsosDetail.Text = park?.QSOs.ToString("N0") ?? "—";
        AttemptsDetail.Text = park?.Attempts.ToString("N0") ?? "—";
        LatitudeDetail.Text = park?.Latitude.ToString("F5") ?? "—";
        LongitudeDetail.Text = park?.Longitude.ToString("F5") ?? "—";
        GoogleMapsButton.IsEnabled = park is not null;
        AddToRouteButton.IsEnabled = _routePlanningMode
            && park is not null
            && !_plannedReferences.Contains(park.Reference);
    }

    private void SetSelectedPark(Park? park, bool zoomToPark = true)
    {
        _selectedPark = park;
        UpdateParkDetails(park);
        UpdateSelectedMarker(park);

        if (park is not null && zoomToPark)
            ZoomToPark(park);
    }

    private void ClearParkSelection()
    {
        ParksGrid.SelectedItem = null;
        RouteStopsGrid.SelectedItem = null;
        SetSelectedPark(null, zoomToPark: false);
    }

    private void SetRouteStops(IEnumerable<RouteStop> stops)
    {
        _routeStops.Clear();
        foreach (var stop in stops.OrderBy(stop => stop.RoutePositionKm).ThenBy(stop => stop.DistanceFromRouteKm))
            _routeStops.Add(stop);

        UpdateRouteStopsHeader();
    }

    private void UpdateRouteStopsHeader() =>
        RouteStopsGroup.Header = $"Route Stops ({_routeStops.Count:N0})";

    private void PopulateProvinceSelector()
    {
        _provinceOptions.Clear();
        foreach (string code in _parks
                     .Select(GetProvinceCode)
                     .Where(code => !string.IsNullOrWhiteSpace(code))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(code => GetProvinceName(code), StringComparer.OrdinalIgnoreCase))
        {
            _provinceOptions.Add(new ProvinceOption(code, GetProvinceName(code)));
        }

        _selectedProvinceCodes.Clear();
        ProvinceListBox.SelectedItems.Clear();
        UpdateProvinceButtonText();
    }

    private bool MatchesSelectedProvinces(Park park) =>
        _selectedProvinceCodes.Count == 0 || _selectedProvinceCodes.Contains(GetProvinceCode(park));

    private static string GetProvinceCode(Park park)
    {
        string location = park.LocationDescription?.Trim() ?? string.Empty;
        return location.StartsWith("CA-", StringComparison.OrdinalIgnoreCase) && location.Length >= 5
            ? location[3..]
            : string.Empty;
    }

    private static string GetProvinceName(string code) => code.ToUpperInvariant() switch
    {
        "AB" => "Alberta",
        "BC" => "British Columbia",
        "MB" => "Manitoba",
        "NB" => "New Brunswick",
        "NL" => "Newfoundland and Labrador",
        "NS" => "Nova Scotia",
        "NT" => "Northwest Territories",
        "NU" => "Nunavut",
        "ON" => "Ontario",
        "PE" => "Prince Edward Island",
        "QC" => "Quebec",
        "SK" => "Saskatchewan",
        "YT" => "Yukon",
        _ => code
    };

    private void UpdateProvinceButtonText()
    {
        ProvinceButton.Content = _selectedProvinceCodes.Count switch
        {
            0 => "Provinces: All Canada",
            1 => $"Province: {GetProvinceName(_selectedProvinceCodes.Single())}",
            _ => $"Provinces: {_selectedProvinceCodes.Count} selected"
        };
    }

    private static void OpenGoogleMaps(Park park)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = park.GoogleMapsUrl, UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show("Unable to open Google Maps.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var parks = _viewSource.View.Cast<Park>().ToList();
        bool exportRoute = _routePlanningMode && _activeRoute is not null;

        if (!exportRoute && parks.Count == 0)
        {
            MessageBox.Show("There are no parks to export.", "Export Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            FileName = exportRoute
                ? $"POTA_Route_{ToFileNamePart(RouteStartBox.Text)}_to_{ToFileNamePart(RouteDestinationBox.Text)}_{DateTime.Now:yyyy-MM-dd}.xlsx"
                : $"POTA_Parks_{DateTime.Now:yyyy-MM-dd}.xlsx"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            if (exportRoute)
            {
                _excelService.ExportRoute(
                    dialog.FileName,
                    _routeStops,
                    RouteStartBox.Text,
                    RouteDestinationBox.Text,
                    _activeRoute!);

                MessageBox.Show($"Successfully exported {_routeStops.Count} planned route stops.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _excelService.Export(dialog.FileName, parks);
                MessageBox.Show($"Successfully exported {parks.Count} parks.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ToFileNamePart(string value)
    {
        string cleaned = new string(value
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray())
            .Trim('_');

        if (string.IsNullOrWhiteSpace(cleaned))
            return "Location";

        return cleaned.Length <= 40 ? cleaned : cleaned[..40];
    }
}

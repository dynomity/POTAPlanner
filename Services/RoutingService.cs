using POTAPlanner.Models;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace POTAPlanner.Services;

public sealed class RoutingService
{
    private static readonly HttpClient Client = CreateHttpClient();

    public async Task<RoutePlan> PlanRouteAsync(string startInput, string destinationInput, double maximumDistanceFromRouteKm)
    {
        if (maximumDistanceFromRouteKm <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumDistanceFromRouteKm), "Distance from route must be greater than zero.");

        var start = await ResolveLocationAsync(startInput);
        var destination = await ResolveLocationAsync(destinationInput);

        string coordinates = string.Join(";", ToCoordinateString(start), ToCoordinateString(destination));
        string url = $"https://router.project-osrm.org/route/v1/driving/{coordinates}?overview=full&geometries=geojson";

        using var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        if (root.GetProperty("code").GetString() != "Ok")
            throw new InvalidOperationException("No driving route was found between those locations.");

        var route = root.GetProperty("routes")[0];
        var points = route.GetProperty("geometry").GetProperty("coordinates")
            .EnumerateArray()
            .Select(coordinate => new RoutePoint(
                coordinate[1].GetDouble(),
                coordinate[0].GetDouble()))
            .ToList();

        return new RoutePlan(
            start,
            destination,
            points,
            route.GetProperty("distance").GetDouble() / 1000d,
            route.GetProperty("duration").GetDouble() / 60d,
            maximumDistanceFromRouteKm);
    }

    public IReadOnlyList<RouteStop> FindStopsAlongRoute(RoutePlan route, IEnumerable<Park> parks)
    {
        return parks
            .Select(park => CreateRouteStop(route, park))
            .Where(stop => stop.DistanceFromRouteKm <= route.MaximumDistanceFromRouteKm)
            .OrderBy(stop => stop.RoutePositionKm)
            .ThenBy(stop => stop.DistanceFromRouteKm)
            .ToList();
    }

    public RouteStop CreateRouteStop(RoutePlan route, Park park)
    {
        var nearest = FindNearestRoutePosition(route.Points, new RoutePoint(park.Latitude, park.Longitude));
        return new RouteStop
        {
            Park = park,
            DistanceFromRouteKm = nearest.DistanceKm,
            RoutePositionKm = nearest.RoutePositionKm,
            RouteDistanceKm = route.DistanceKm
        };
    }

    private static async Task<RoutePoint> ResolveLocationAsync(string input)
    {
        if (TryParseCoordinates(input, out var point))
            return point;

        if (string.IsNullOrWhiteSpace(input))
            throw new InvalidOperationException("Enter a start and destination location.");

        string url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q="
            + Uri.EscapeDataString(input);

        using var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var result = document.RootElement.EnumerateArray().FirstOrDefault();
        if (result.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"Could not find '{input}'. Try a city and province, or use latitude, longitude.");

        return new RoutePoint(
            double.Parse(result.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture),
            double.Parse(result.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture));
    }

    private static bool TryParseCoordinates(string input, out RoutePoint point)
    {
        point = default;
        var values = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 2
            || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude)
            || !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
            return false;

        point = new RoutePoint(latitude, longitude);
        return true;
    }

    private static string ToCoordinateString(RoutePoint point) =>
        $"{point.Longitude.ToString(CultureInfo.InvariantCulture)},{point.Latitude.ToString(CultureInfo.InvariantCulture)}";

    private static NearestRoutePosition FindNearestRoutePosition(IReadOnlyList<RoutePoint> route, RoutePoint park)
    {
        double accumulatedKm = 0;
        double bestDistanceKm = double.MaxValue;
        double bestRoutePositionKm = 0;

        for (int index = 0; index < route.Count - 1; index++)
        {
            var first = route[index];
            var second = route[index + 1];
            double segmentLengthKm = HaversineKm(first, second);
            var projection = ProjectToSegmentKm(park, first, second);

            if (projection.DistanceKm < bestDistanceKm)
            {
                bestDistanceKm = projection.DistanceKm;
                bestRoutePositionKm = accumulatedKm + projection.DistanceAlongSegmentKm;
            }

            accumulatedKm += segmentLengthKm;
        }

        return new NearestRoutePosition(bestDistanceKm, bestRoutePositionKm);
    }

    private static SegmentProjection ProjectToSegmentKm(RoutePoint point, RoutePoint first, RoutePoint second)
    {
        const double kmPerDegreeLatitude = 110.574;
        double kmPerDegreeLongitude = 111.320 * Math.Cos(((first.Latitude + second.Latitude + point.Latitude) / 3d) * Math.PI / 180d);

        double bx = (second.Longitude - first.Longitude) * kmPerDegreeLongitude;
        double by = (second.Latitude - first.Latitude) * kmPerDegreeLatitude;
        double px = (point.Longitude - first.Longitude) * kmPerDegreeLongitude;
        double py = (point.Latitude - first.Latitude) * kmPerDegreeLatitude;
        double lengthSquared = bx * bx + by * by;
        double fraction = lengthSquared == 0 ? 0 : Math.Clamp((px * bx + py * by) / lengthSquared, 0, 1);

        double dx = px - fraction * bx;
        double dy = py - fraction * by;
        return new SegmentProjection(Math.Sqrt(dx * dx + dy * dy), Math.Sqrt(lengthSquared) * fraction);
    }

    private static double HaversineKm(RoutePoint first, RoutePoint second)
    {
        const double earthRadiusKm = 6371.0088;
        double latitudeDelta = DegreesToRadians(second.Latitude - first.Latitude);
        double longitudeDelta = DegreesToRadians(second.Longitude - first.Longitude);
        double a = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + Math.Cos(DegreesToRadians(first.Latitude))
            * Math.Cos(DegreesToRadians(second.Latitude))
            * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return earthRadiusKm * 2 * Math.Asin(Math.Sqrt(a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("POTAPlanner/0.6 (personal trip planning application)");
        return client;
    }

    private readonly record struct NearestRoutePosition(double DistanceKm, double RoutePositionKm);
    private readonly record struct SegmentProjection(double DistanceKm, double DistanceAlongSegmentKm);
}

public sealed record RoutePlan(
    RoutePoint Start,
    RoutePoint Destination,
    IReadOnlyList<RoutePoint> Points,
    double DistanceKm,
    double DurationMinutes,
    double MaximumDistanceFromRouteKm);

public readonly record struct RoutePoint(double Latitude, double Longitude);

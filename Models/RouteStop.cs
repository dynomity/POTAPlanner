namespace POTAPlanner.Models;

public class RouteStop
{
    public Park Park { get; init; } = new();
    public double DistanceFromRouteKm { get; init; }
    public double RoutePositionKm { get; init; }
    public double RouteDistanceKm { get; init; }

    public string Reference => Park.Reference;
    public string Name => Park.Name;
    public int Activations => Park.Activations;
    public string DistanceFromRoute => $"{DistanceFromRouteKm:N1} km";
    public string DistanceFromStart => $"{RoutePositionKm:N1} km";
    public string DistanceToDestination => $"{Math.Max(0, RouteDistanceKm - RoutePositionKm):N1} km";
}

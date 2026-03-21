namespace InterplanetaryManeuver.App.Models;

public sealed class EphemerisState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DateTime EpochUtc { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
    public required double Vx { get; init; }
    public required double Vy { get; init; }
    public required double Vz { get; init; }
}


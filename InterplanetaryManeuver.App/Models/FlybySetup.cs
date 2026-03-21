namespace InterplanetaryManeuver.App.Models;

public sealed class FlybySetup
{
    public double StartDistanceMultiplier { get; init; } = 1.20;
    public double PhaseAngleDeg { get; init; } = -35.0;
    public double HeadingAngleDeg { get; init; } = 11.0;
    public double VInfinityKms { get; init; } = 9.5;
}


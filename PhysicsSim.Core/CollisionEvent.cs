namespace PhysicsSim.Core;

public sealed class CollisionEvent
{
    public required int BodyAIndex { get; init; }
    public required int BodyBIndex { get; init; }
    public required string BodyAName { get; init; }
    public required string BodyBName { get; init; }
    public required double Time { get; init; }
    public required double Distance { get; init; }
    public required double ThresholdDistance { get; init; }
}

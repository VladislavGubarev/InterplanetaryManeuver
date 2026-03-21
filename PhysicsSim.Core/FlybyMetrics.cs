namespace PhysicsSim.Core;

public sealed class FlybyMetrics
{
    public required double JupiterSoiRadius { get; init; }
    public required double MinDistanceToJupiter { get; init; }
    public required double MinDistanceToSaturn { get; init; }
    public required double InitialHeliocentricSpeed { get; init; }
    public required double FinalHeliocentricSpeed { get; init; }
    public required double InitialJupiterRelativeSpeed { get; init; }
    public required double FinalJupiterRelativeSpeed { get; init; }
    public required double DeltaVGainHeliocentric { get; init; }
    public required int EntryIndex { get; init; }
    public required int ExitIndex { get; init; }
    public required int ClosestApproachIndex { get; init; }

    public bool HasSphereOfInfluenceCrossing => EntryIndex >= 0 && ExitIndex > EntryIndex;
}


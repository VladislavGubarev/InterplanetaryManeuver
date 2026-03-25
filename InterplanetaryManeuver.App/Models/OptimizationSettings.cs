namespace InterplanetaryManeuver.App.Models;

public sealed class OptimizationSettings
{
    public required double PhaseMinDeg { get; init; }
    public required double PhaseMaxDeg { get; init; }
    public required int PhaseSamples { get; init; }
    public required double HeadingMinDeg { get; init; }
    public required double HeadingMaxDeg { get; init; }
    public required int HeadingSamples { get; init; }
    public required double VInfinityMinKms { get; init; }
    public required double VInfinityMaxKms { get; init; }
    public required int VInfinitySamples { get; init; }
    public required bool UseLocalRefinement { get; init; }
    public required int LocalIterations { get; init; }
    public required double GradientNormTolerance { get; init; }
    public required double PhaseDerivativeStepDeg { get; init; }
    public required double HeadingDerivativeStepDeg { get; init; }
    public required double VInfinityDerivativeStepKms { get; init; }
    public required double PhaseMoveStepDeg { get; init; }
    public required double HeadingMoveStepDeg { get; init; }
    public required double VInfinityMoveStepKms { get; init; }

    public int TotalSamples => PhaseSamples * HeadingSamples * VInfinitySamples;
}

namespace InterplanetaryManeuver.App.Models;

public sealed class OptimizationCandidate
{
    public required int Index { get; init; }
    public required double PhaseAngleDeg { get; init; }
    public required double HeadingAngleDeg { get; init; }
    public required double VInfinityKms { get; init; }
    public required double DeltaVGainKms { get; init; }
    public required double MinJupiterDistanceKm { get; init; }
    public required double MinSaturnDistanceAu { get; init; }
    public required double Score { get; init; }
    public required string Status { get; init; }

    public override string ToString()
    {
        return $"#{Index}: {Status}, score={Score:F3}, Δv={DeltaVGainKms:F3} км/с, " +
               $"rJmin={MinJupiterDistanceKm:n0} км, rSmin={MinSaturnDistanceAu:F3} а.е., " +
               $"phi={PhaseAngleDeg:F1}°, alpha={HeadingAngleDeg:F1}°, v∞={VInfinityKms:F2} км/с";
    }
}

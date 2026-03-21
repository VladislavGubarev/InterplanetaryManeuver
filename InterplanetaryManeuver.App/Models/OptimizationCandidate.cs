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

    public override string ToString()
    {
        return $"#{Index}: score={Score:F3}, dV={DeltaVGainKms:F3} km/s, " +
               $"rJmin={MinJupiterDistanceKm:n0} km, rSmin={MinSaturnDistanceAu:F3} AU, " +
               $"phi={PhaseAngleDeg:F1} deg, alpha={HeadingAngleDeg:F1} deg, vInf={VInfinityKms:F2} km/s";
    }
}


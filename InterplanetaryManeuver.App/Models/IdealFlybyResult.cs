using InterplanetaryManeuver.App.Models;

namespace InterplanetaryManeuver.App.Models;

public sealed class IdealFlybyResult
{
    public required IReadOnlyList<LineSeries> OrbitSeries { get; init; }
    public required IReadOnlyList<LineSeries> SpeedSeries { get; init; }
    public required IReadOnlyList<LineSeries> SpeedComponentSeries { get; init; }
    public required string StatusText { get; init; }
    public required string MetricsText { get; init; }
    public required string ReportText { get; init; }
}

using System.Windows;
using System.Windows.Media;

namespace InterplanetaryManeuver.App.Models;

public sealed class LineSeries
{
    public required string Name { get; init; }
    public required IReadOnlyList<Point> Points { get; init; }
    public required Brush Stroke { get; init; }
    public double Thickness { get; init; } = 2.0;
}


using System.Windows.Media;
using PhysicsSim.Core;

namespace InterplanetaryManeuver.App.Models;

public sealed class AnimationSceneData
{
    public required Vector3d[][] Positions { get; init; }
    public required string[] BodyNames { get; init; }
    public required Brush[] BodyBrushes { get; init; }
    public required int CenterBodyIndex { get; init; }
}


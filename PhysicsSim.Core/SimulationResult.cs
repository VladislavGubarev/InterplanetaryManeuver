namespace PhysicsSim.Core;

public sealed class SimulationResult
{
    public required double[] Times { get; init; }
    public required string[] BodyNames { get; init; }
    public required Vector3d[][] Positions { get; init; } // [момент времени][тело]
    public required Vector3d[][] Velocities { get; init; } // [момент времени][тело]
    public CollisionEvent? Collision { get; init; }
    public string? TerminationReason { get; init; }

    public int SampleCount => Times.Length;
    public int BodyCount => BodyNames.Length;
    public bool TerminatedEarly => Collision is not null || !string.IsNullOrWhiteSpace(TerminationReason);
}

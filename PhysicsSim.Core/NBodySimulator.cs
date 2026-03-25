using PhysicsSim.Core.Ode;

namespace PhysicsSim.Core;

public static class NBodySimulator
{
    public static SimulationResult Simulate(
        NBodySystem system,
        double t0,
        double t1,
        double outputDt,
        IntegrationSettings settings,
        IReadOnlyList<double>? collisionRadii = null,
        CancellationToken cancellationToken = default)
    {
        var times = new List<double>(capacity: (int)Math.Ceiling((t1 - t0) / outputDt) + 1);
        var positions = new List<Vector3d[]>();
        var velocities = new List<Vector3d[]>();
        CollisionEvent? collision = null;

        string? terminationReason = DormandPrince45.IntegrateFixedOutput(
            system,
            t0,
            system.InitialState,
            t1,
            outputDt,
            settings,
            (t, y) =>
            {
                times.Add(t);
                var pos = new Vector3d[system.BodyCount];
                var vel = new Vector3d[system.BodyCount];
                for (int i = 0; i < system.BodyCount; i++)
                {
                    int b = i * 6;
                    pos[i] = new Vector3d(y[b + 0], y[b + 1], y[b + 2]);
                    vel[i] = new Vector3d(y[b + 3], y[b + 4], y[b + 5]);
                }

                positions.Add(pos);
                velocities.Add(vel);
            },
            (t, state) =>
            {
                collision = TryDetectCollision(system, state, collisionRadii, t);
                if (collision is null)
                    return null;

                return $"Столкновение: {collision.BodyAName} ↔ {collision.BodyBName}";
            },
            cancellationToken);

        return new SimulationResult
        {
            Times = times.ToArray(),
            BodyNames = system.Names.ToArray(),
            Positions = positions.ToArray(),
            Velocities = velocities.ToArray(),
            Collision = collision,
            TerminationReason = terminationReason,
        };
    }

    private static CollisionEvent? TryDetectCollision(
        NBodySystem system,
        double[] state,
        IReadOnlyList<double>? collisionRadii,
        double time)
    {
        if (collisionRadii is null || collisionRadii.Count != system.BodyCount)
            return null;

        for (int i = 0; i < system.BodyCount; i++)
        {
            double ri = Math.Max(0.0, collisionRadii[i]);
            for (int j = i + 1; j < system.BodyCount; j++)
            {
                double threshold = ri + Math.Max(0.0, collisionRadii[j]);
                if (threshold <= 0.0)
                    continue;

                int bi = i * 6;
                int bj = j * 6;
                double dx = state[bi + 0] - state[bj + 0];
                double dy = state[bi + 1] - state[bj + 1];
                double dz = state[bi + 2] - state[bj + 2];
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                if (distance <= threshold)
                {
                    return new CollisionEvent
                    {
                        BodyAIndex = i,
                        BodyBIndex = j,
                        BodyAName = system.Names[i],
                        BodyBName = system.Names[j],
                        Time = time,
                        Distance = distance,
                        ThresholdDistance = threshold,
                    };
                }
            }
        }

        return null;
    }
}

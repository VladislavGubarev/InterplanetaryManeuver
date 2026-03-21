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
        CancellationToken cancellationToken = default)
    {
        var times = new List<double>(capacity: (int)Math.Ceiling((t1 - t0) / outputDt) + 1);
        var positions = new List<Vector3d[]>();
        var velocities = new List<Vector3d[]>();

        DormandPrince45.IntegrateFixedOutput(
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
            cancellationToken);

        return new SimulationResult
        {
            Times = times.ToArray(),
            BodyNames = system.Names.ToArray(),
            Positions = positions.ToArray(),
            Velocities = velocities.ToArray(),
        };
    }
}

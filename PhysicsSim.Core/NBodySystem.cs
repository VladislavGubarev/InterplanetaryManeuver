namespace PhysicsSim.Core;

/// <summary>
/// Ньютоновская N-body модель гравитации:
/// r_i' = v_i
/// v_i' = sum_{j!=i} G*m_j*(r_j - r_i)/|r_j-r_i|^3
/// </summary>
public sealed class NBodySystem : IOdeSystem
{
    public double GravitationalConstant { get; }
    public string[] Names { get; }
    public double[] Masses { get; }

    public int BodyCount => Masses.Length;
    public int Dimension => BodyCount * 6; // По 6 компонент на тело: (x,y,z,vx,vy,vz)

    public NBodySystem(double gravitationalConstant, IReadOnlyList<BodyState> bodies, bool toBarycentricFrame = true)
    {
        if (bodies.Count < 2)
            throw new ArgumentException("Need at least 2 bodies.", nameof(bodies));

        GravitationalConstant = gravitationalConstant;
        Names = new string[bodies.Count];
        Masses = new double[bodies.Count];

        for (int i = 0; i < bodies.Count; i++)
        {
            Names[i] = bodies[i].Name;
            Masses[i] = bodies[i].Mass;
        }

        InitialState = new double[Dimension];
        for (int i = 0; i < bodies.Count; i++)
        {
            WriteBodyState(InitialState, i, bodies[i].Position, bodies[i].Velocity);
        }

        if (toBarycentricFrame)
            ShiftInitialStateToBarycentricFrame();
    }

    /// <summary>
    /// Начальный вектор состояния в барицентрической системе отсчёта (если включена).
    /// Формат: [x,y,z,vx,vy,vz] для каждого тела подряд.
    /// </summary>
    public double[] InitialState { get; }

    public void ComputeDerivatives(double t, ReadOnlySpan<double> y, Span<double> dy)
    {
        if (y.Length != Dimension) throw new ArgumentException("Invalid state length.", nameof(y));
        if (dy.Length != Dimension) throw new ArgumentException("Invalid derivative length.", nameof(dy));

        // Производные координат равны скоростям.
        for (int i = 0; i < BodyCount; i++)
        {
            int baseIdx = i * 6;
            dy[baseIdx + 0] = y[baseIdx + 3];
            dy[baseIdx + 1] = y[baseIdx + 4];
            dy[baseIdx + 2] = y[baseIdx + 5];
        }

        // Производные скоростей определяются суммой гравитационных ускорений.
        for (int i = 0; i < BodyCount; i++)
        {
            int bi = i * 6;
            double ax = 0, ay = 0, az = 0;

            double xi = y[bi + 0];
            double yi = y[bi + 1];
            double zi = y[bi + 2];

            for (int j = 0; j < BodyCount; j++)
            {
                if (j == i) continue;
                double mj = Masses[j];
                if (mj == 0) continue;

                int bj = j * 6;
                double dx = y[bj + 0] - xi;
                double dyv = y[bj + 1] - yi;
                double dz = y[bj + 2] - zi;

                double r2 = dx * dx + dyv * dyv + dz * dz;
                if (r2 == 0) continue;

                double invR = 1.0 / Math.Sqrt(r2);
                double invR3 = invR * invR * invR;
                double k = GravitationalConstant * mj * invR3;

                ax += k * dx;
                ay += k * dyv;
                az += k * dz;
            }

            dy[bi + 3] = ax;
            dy[bi + 4] = ay;
            dy[bi + 5] = az;
        }
    }

    public BodyState ReadBodyFromState(ReadOnlySpan<double> state, int bodyIndex)
    {
        int b = bodyIndex * 6;
        var position = new Vector3d(state[b + 0], state[b + 1], state[b + 2]);
        var velocity = new Vector3d(state[b + 3], state[b + 4], state[b + 5]);
        return new BodyState(Names[bodyIndex], Masses[bodyIndex], position, velocity);
    }

    public static void WriteBodyState(Span<double> state, int bodyIndex, Vector3d position, Vector3d velocity)
    {
        int b = bodyIndex * 6;
        state[b + 0] = position.X;
        state[b + 1] = position.Y;
        state[b + 2] = position.Z;
        state[b + 3] = velocity.X;
        state[b + 4] = velocity.Y;
        state[b + 5] = velocity.Z;
    }

    public void ShiftInitialStateToBarycentricFrame()
    {
        var comPos = Vector3d.Zero;
        var comVel = Vector3d.Zero;
        double mTotal = 0;

        for (int i = 0; i < BodyCount; i++)
        {
            double m = Masses[i];
            if (m <= 0) continue;
            mTotal += m;
            int b = i * 6;
            comPos += m * new Vector3d(InitialState[b + 0], InitialState[b + 1], InitialState[b + 2]);
            comVel += m * new Vector3d(InitialState[b + 3], InitialState[b + 4], InitialState[b + 5]);
        }

        if (mTotal == 0) return;
        comPos /= mTotal;
        comVel /= mTotal;

        for (int i = 0; i < BodyCount; i++)
        {
            int b = i * 6;
            InitialState[b + 0] -= comPos.X;
            InitialState[b + 1] -= comPos.Y;
            InitialState[b + 2] -= comPos.Z;
            InitialState[b + 3] -= comVel.X;
            InitialState[b + 4] -= comVel.Y;
            InitialState[b + 5] -= comVel.Z;
        }
    }
}

using System.Runtime.CompilerServices;

namespace PhysicsSim.Core.Ode;

public delegate void SampleHandler(double t, ReadOnlySpan<double> y);
public delegate string? StopCondition(double t, double[] y);

/// <summary>
/// Адаптивный интегратор Dormand-Prince RK 5(4) с контролем локальной ошибки.
/// Внутри шагает с переменным шагом, но наружу выдает значения на фиксированной сетке времени.
/// Это удобно для графиков и анимации: численный метод остается точным, а данные — равномерными.
/// </summary>
public static class DormandPrince45
{
    public static string? IntegrateFixedOutput(
        IOdeSystem system,
        double t0,
        ReadOnlySpan<double> y0,
        double t1,
        double outputDt,
        IntegrationSettings settings,
        SampleHandler onSample,
        StopCondition? stopCondition = null,
        CancellationToken cancellationToken = default)
    {
        if (t1 <= t0) throw new ArgumentException("t1 must be > t0.", nameof(t1));
        if (outputDt <= 0) throw new ArgumentException("outputDt must be > 0.", nameof(outputDt));
        if (y0.Length != system.Dimension) throw new ArgumentException("State dimension mismatch.", nameof(y0));

        int n = system.Dimension;
        double[] y = y0.ToArray();
        double[] yNew = new double[n];
        double[] yErr = new double[n];

        double[] k1 = new double[n];
        double[] k2 = new double[n];
        double[] k3 = new double[n];
        double[] k4 = new double[n];
        double[] k5 = new double[n];
        double[] k6 = new double[n];
        double[] k7 = new double[n];
        double[] yTemp = new double[n];

        double t = t0;
        double nextOut = t0;
        double h = Clamp(settings.InitialStep, settings.MinStep, settings.MaxStep);
        int accepted = 0;
        bool emittedT0 = false;
        bool emittedT1 = false;
        double lastSampleTime = double.NaN;

        void EmitSample(double time, double[] state)
        {
            if (!double.IsNaN(lastSampleTime) && Math.Abs(time - lastSampleTime) < 1e-12)
                return;

            onSample(time, state);
            lastSampleTime = time;
        }

        string? CheckStop()
        {
            if (stopCondition is null)
                return null;

            return stopCondition(t, y);
        }

        while (!emittedT1)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!emittedT0)
            {
                EmitSample(t, y);
                emittedT0 = true;
                string? stopAtStart = CheckStop();
                if (stopAtStart is not null)
                    return stopAtStart;

                nextOut = t0 + outputDt;
                if (nextOut >= t1)
                    nextOut = t1;
            }

            while (t < nextOut - 1e-12)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (accepted > settings.MaxAcceptedSteps)
                    throw new InvalidOperationException("Exceeded max accepted steps. Relax tolerances or reduce interval.");

                double hToTarget = nextOut - t;
                if (h > hToTarget)
                    h = hToTarget;

                h = Clamp(h, settings.MinStep, settings.MaxStep);

                bool ok = TryStep(system, t, y, h, settings.AbsTol, settings.RelTol, yNew, yErr, k1, k2, k3, k4, k5, k6, k7, yTemp, out double errNorm);
                if (ok)
                {
                    Array.Copy(yNew, y, n);
                    t += h;
                    accepted++;

                    string? stopReason = CheckStop();
                    if (stopReason is not null)
                    {
                        EmitSample(t, y);
                        return stopReason;
                    }
                }

                // Даже если шаг отклонен, оценка ошибки полезна:
                // по ней выбираем следующий размер шага.
                h = SuggestNextStep(h, errNorm, ok);
            }

            t = nextOut;
            if (Math.Abs(t - t1) < 1e-12)
            {
                EmitSample(t1, y);
                emittedT1 = true;
            }
            else
            {
                EmitSample(t, y);
                string? stopReason = CheckStop();
                if (stopReason is not null)
                    return stopReason;

                nextOut = t + outputDt;
                if (nextOut >= t1)
                    nextOut = t1;
            }
        }

        return null;
    }

    private static bool TryStep(
        IOdeSystem system,
        double t,
        ReadOnlySpan<double> y,
        double h,
        double absTol,
        double relTol,
        Span<double> yNew,
        Span<double> yErr,
        Span<double> k1,
        Span<double> k2,
        Span<double> k3,
        Span<double> k4,
        Span<double> k5,
        Span<double> k6,
        Span<double> k7,
        Span<double> yTemp,
        out double errNorm)
    {
        int n = y.Length;

        system.ComputeDerivatives(t, y, k1);

        Combine(y, h, k1, 1.0 / 5.0, yTemp);
        system.ComputeDerivatives(t + h * (1.0 / 5.0), yTemp, k2);

        Combine(y, h, k1, 3.0 / 40.0, k2, 9.0 / 40.0, yTemp);
        system.ComputeDerivatives(t + h * (3.0 / 10.0), yTemp, k3);

        Combine(y, h, k1, 44.0 / 45.0, k2, -56.0 / 15.0, k3, 32.0 / 9.0, yTemp);
        system.ComputeDerivatives(t + h * (4.0 / 5.0), yTemp, k4);

        Combine(y, h, k1, 19372.0 / 6561.0, k2, -25360.0 / 2187.0, k3, 64448.0 / 6561.0, k4, -212.0 / 729.0, yTemp);
        system.ComputeDerivatives(t + h * (8.0 / 9.0), yTemp, k5);

        Combine(y, h, k1, 9017.0 / 3168.0, k2, -355.0 / 33.0, k3, 46732.0 / 5247.0, k4, 49.0 / 176.0, k5, -5103.0 / 18656.0, yTemp);
        system.ComputeDerivatives(t + h, yTemp, k6);

        // yNew — решение пятого порядка. Оно используется как основное.
        Combine(y, h, k1, 35.0 / 384.0, k3, 500.0 / 1113.0, k4, 125.0 / 192.0, k5, -2187.0 / 6784.0, k6, 11.0 / 84.0, yNew);
        system.ComputeDerivatives(t + h, yNew, k7);

        for (int i = 0; i < n; i++)
        {
            // y4 — вложенное решение четвертого порядка.
            // Разность y5 - y4 дает оценку локальной ошибки на этом шаге.
            double y4 = y[i]
                        + h * (5179.0 / 57600.0) * k1[i]
                        + h * (7571.0 / 16695.0) * k3[i]
                        + h * (393.0 / 640.0) * k4[i]
                        + h * (-92097.0 / 339200.0) * k5[i]
                        + h * (187.0 / 2100.0) * k6[i]
                        + h * (1.0 / 40.0) * k7[i];

            yErr[i] = yNew[i] - y4;
        }

        errNorm = ComputeErrorNorm(y, yNew, yErr, absTol, relTol);
        return errNorm <= 1.0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SuggestNextStep(double h, double errNorm, bool accepted)
    {
        const double safety = 0.9;
        const double facMin = 0.2;
        const double facMax = 5.0;
        const double pow = 0.2;

        if (errNorm <= 0)
            errNorm = 1e-16;

        double fac = safety * Math.Pow(1.0 / errNorm, pow);
        fac = Clamp(fac, facMin, facMax);
        double hNew = h * fac;
        if (!accepted)
            hNew = Math.Min(hNew, h);

        return hNew;
    }

    private static double ComputeErrorNorm(ReadOnlySpan<double> y, ReadOnlySpan<double> yNew, ReadOnlySpan<double> yErr, double absTol, double relTol)
    {
        double sum = 0;
        int n = y.Length;
        for (int i = 0; i < n; i++)
        {
            double scale = absTol + relTol * Math.Max(Math.Abs(y[i]), Math.Abs(yNew[i]));
            double r = yErr[i] / scale;
            sum += r * r;
        }

        return Math.Sqrt(sum / n);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Combine(ReadOnlySpan<double> y, double h, ReadOnlySpan<double> k1, double a1, Span<double> dest)
    {
        for (int i = 0; i < y.Length; i++)
            dest[i] = y[i] + h * a1 * k1[i];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Combine(ReadOnlySpan<double> y, double h, ReadOnlySpan<double> k1, double a1, ReadOnlySpan<double> k2, double a2, Span<double> dest)
    {
        for (int i = 0; i < y.Length; i++)
            dest[i] = y[i] + h * (a1 * k1[i] + a2 * k2[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Combine(ReadOnlySpan<double> y, double h, ReadOnlySpan<double> k1, double a1, ReadOnlySpan<double> k2, double a2, ReadOnlySpan<double> k3, double a3, Span<double> dest)
    {
        for (int i = 0; i < y.Length; i++)
            dest[i] = y[i] + h * (a1 * k1[i] + a2 * k2[i] + a3 * k3[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Combine(ReadOnlySpan<double> y, double h, ReadOnlySpan<double> k1, double a1, ReadOnlySpan<double> k2, double a2, ReadOnlySpan<double> k3, double a3, ReadOnlySpan<double> k4, double a4, Span<double> dest)
    {
        for (int i = 0; i < y.Length; i++)
            dest[i] = y[i] + h * (a1 * k1[i] + a2 * k2[i] + a3 * k3[i] + a4 * k4[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Combine(ReadOnlySpan<double> y, double h, ReadOnlySpan<double> k1, double a1, ReadOnlySpan<double> k2, double a2, ReadOnlySpan<double> k3, double a3, ReadOnlySpan<double> k4, double a4, ReadOnlySpan<double> k5, double a5, Span<double> dest)
    {
        for (int i = 0; i < y.Length; i++)
            dest[i] = y[i] + h * (a1 * k1[i] + a2 * k2[i] + a3 * k3[i] + a4 * k4[i] + a5 * k5[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Combine(ReadOnlySpan<double> y, double h, ReadOnlySpan<double> k1, double a1, ReadOnlySpan<double> k2, double a2, ReadOnlySpan<double> k3, double a3, ReadOnlySpan<double> k4, double a4, ReadOnlySpan<double> k5, double a5, ReadOnlySpan<double> k6, double a6, Span<double> dest)
    {
        for (int i = 0; i < y.Length; i++)
            dest[i] = y[i] + h * (a1 * k1[i] + a2 * k2[i] + a3 * k3[i] + a4 * k4[i] + a5 * k5[i] + a6 * k6[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
}

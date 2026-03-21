namespace PhysicsSim.Core;

public interface IOdeSystem
{
    int Dimension { get; }
    void ComputeDerivatives(double t, ReadOnlySpan<double> y, Span<double> dy);
}


namespace PhysicsSim.Core.Ode;

public sealed class IntegrationSettings
{
    public double AbsTol { get; init; } = 1e-3;
    public double RelTol { get; init; } = 1e-9;
    public double MinStep { get; init; } = 1e-6;
    public double MaxStep { get; init; } = 1e9;
    public double InitialStep { get; init; } = 10.0;
    public int MaxAcceptedSteps { get; init; } = 2_000_000;
}


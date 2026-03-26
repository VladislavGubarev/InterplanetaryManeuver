namespace InterplanetaryManeuver.App.ViewModels;

public enum SimulationPresetKind
{
    IdealFlyby,
    JupiterFlyby,
    ExtendedJupiterFlyby,
    SolarSystemBodies,
}

public sealed class SimulationScenario
{
    public required string Name { get; init; }
    public required IReadOnlyList<PhysicsSim.Core.BodyState> Bodies { get; init; }
    public required IReadOnlyList<double> BodyCollisionRadii { get; init; }
    public required int SunIndex { get; init; }
    public required int JupiterIndex { get; init; }
    public required int SaturnIndex { get; init; }
    public required int SpacecraftIndex { get; init; }
    public required DateTime EpochUtc { get; init; }
    public required bool UsesEphemerides { get; init; }
    public required double JupiterSoiRadius { get; init; }
    public double GravitationalConstant { get; init; } = 6.67430e-11;
    public bool ToBarycentricFrame { get; init; } = true;
}

public sealed class SimulationPreset
{
    public required string Name { get; init; }
    public required SimulationPresetKind Kind { get; init; }
    public override string ToString() => Name;

    public static IReadOnlyList<SimulationPreset> CreateDefaults()
    {
        return
        [
            new SimulationPreset
            {
                Name = "Гравиманевр у Юпитера (КА, Horizons)",
                Kind = SimulationPresetKind.JupiterFlyby,
            },
            new SimulationPreset
            {
                Name = "Расширенная система: 8 планет + КА",
                Kind = SimulationPresetKind.ExtendedJupiterFlyby,
            },
            new SimulationPreset
            {
                Name = "Идеальный flyby (аналитика)",
                Kind = SimulationPresetKind.IdealFlyby,
            },
            new SimulationPreset
            {
                Name = "Солнце + Юпитер + Сатурн (Horizons)",
                Kind = SimulationPresetKind.SolarSystemBodies,
            },
        ];
    }
}


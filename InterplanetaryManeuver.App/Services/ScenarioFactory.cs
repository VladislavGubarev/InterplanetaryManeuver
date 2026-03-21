using InterplanetaryManeuver.App.Models;
using InterplanetaryManeuver.App.ViewModels;
using PhysicsSim.Core;

namespace InterplanetaryManeuver.App.Services;

public sealed class ScenarioFactory
{
    private readonly HorizonsEphemerisService _ephemerisService;

    public ScenarioFactory(HorizonsEphemerisService ephemerisService)
    {
        _ephemerisService = ephemerisService;
    }

    public async Task<SimulationScenario> CreateAsync(
        SimulationPresetKind kind,
        DateTime epochUtc,
        FlybySetup? flybySetup,
        CancellationToken cancellationToken)
    {
        var sunState = await _ephemerisService.GetStateAsync("10", "Солнце", epochUtc, cancellationToken);
        var jupiterState = await _ephemerisService.GetStateAsync("599", "Юпитер", epochUtc, cancellationToken);
        var saturnState = await _ephemerisService.GetStateAsync("699", "Сатурн", epochUtc, cancellationToken);

        double jupiterSoiRadius = FlybyAnalysis.ComputeSphereOfInfluenceRadius(
            AstronomyConstants.JupiterMass,
            AstronomyConstants.SolarMass,
            AstronomyConstants.JupiterSemiMajorAxis);

        var sun = CreateBody(sunState, AstronomyConstants.SolarMass);
        var jupiter = CreateBody(jupiterState, AstronomyConstants.JupiterMass);
        var saturn = CreateBody(saturnState, AstronomyConstants.SaturnMass);

        var bodies = new List<BodyState> { sun, jupiter, saturn };
        int spacecraftIndex = -1;

        if (kind == SimulationPresetKind.JupiterFlyby)
        {
            FlybySetup setup = flybySetup ?? new FlybySetup();
            BodyState spacecraft = CreateSpacecraft(jupiter, sun, setup, jupiterSoiRadius);
            bodies.Add(spacecraft);
            spacecraftIndex = bodies.Count - 1;
        }

        return new SimulationScenario
        {
            Name = kind == SimulationPresetKind.JupiterFlyby
                ? "Гравиманевр у Юпитера"
                : "Солнце + Юпитер + Сатурн",
            Bodies = bodies,
            SunIndex = 0,
            JupiterIndex = 1,
            SaturnIndex = 2,
            SpacecraftIndex = spacecraftIndex,
            EpochUtc = epochUtc,
            UsesEphemerides = true,
            JupiterSoiRadius = jupiterSoiRadius,
            ToBarycentricFrame = true,
        };
    }

    private static BodyState CreateBody(EphemerisState state, double mass)
    {
        return new BodyState(
            state.Name,
            mass,
            new Vector3d(state.X, state.Y, state.Z),
            new Vector3d(state.Vx, state.Vy, state.Vz));
    }

    private static BodyState CreateSpacecraft(BodyState jupiter, BodyState sun, FlybySetup setup, double jupiterSoiRadius)
    {
        Vector3d sunToJupiter = (jupiter.Position - sun.Position).Normalized();
        Vector3d orbitalDirection = (jupiter.Velocity - sun.Velocity).Normalized();
        Vector3d normal = Vector3d.Cross(sunToJupiter, orbitalDirection).Normalized();
        if (normal.Length() < 1e-12)
        {
            normal = new Vector3d(0, 0, 1);
        }

        Vector3d tangent = Vector3d.Cross(normal, sunToJupiter).Normalized();
        double phase = DegreesToRadians(setup.PhaseAngleDeg);
        Vector3d radial = (Math.Cos(phase) * sunToJupiter + Math.Sin(phase) * tangent).Normalized();
        Vector3d lateral = Vector3d.Cross(normal, radial).Normalized();

        double startDistance = Math.Max(1.05, setup.StartDistanceMultiplier) * jupiterSoiRadius;
        Vector3d relPosition = radial * startDistance;

        double heading = DegreesToRadians(setup.HeadingAngleDeg);
        Vector3d inward = (-radial).Normalized();
        Vector3d relVelocityDir = (Math.Cos(heading) * inward + Math.Sin(heading) * lateral).Normalized();
        Vector3d relVelocity = relVelocityDir * (setup.VInfinityKms * 1000.0);

        return new BodyState(
            "КА",
            0.0,
            jupiter.Position + relPosition,
            jupiter.Velocity + relVelocity);
    }

    private static double DegreesToRadians(double value) => Math.PI * value / 180.0;
}


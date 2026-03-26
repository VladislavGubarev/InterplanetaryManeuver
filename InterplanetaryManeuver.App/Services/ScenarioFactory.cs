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
        return kind switch
        {
            SimulationPresetKind.ExtendedJupiterFlyby => await CreateExtendedFlybyAsync(epochUtc, flybySetup, cancellationToken),
            _ => await CreateBaseScenarioAsync(kind, epochUtc, flybySetup, cancellationToken),
        };
    }

    private async Task<SimulationScenario> CreateBaseScenarioAsync(
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
        var collisionRadii = new List<double>
        {
            AstronomyConstants.SolarRadius,
            AstronomyConstants.JupiterMeanRadius,
            AstronomyConstants.SaturnMeanRadius,
        };

        int spacecraftIndex = -1;
        if (kind == SimulationPresetKind.JupiterFlyby)
        {
            FlybySetup setup = flybySetup ?? new FlybySetup();
            bodies.Add(CreateSpacecraft(jupiter, sun, setup, jupiterSoiRadius));
            collisionRadii.Add(0.0);
            spacecraftIndex = bodies.Count - 1;
        }

        return new SimulationScenario
        {
            Name = kind == SimulationPresetKind.JupiterFlyby
                ? "Гравиманевр у Юпитера"
                : "Солнце + Юпитер + Сатурн",
            Bodies = bodies,
            BodyCollisionRadii = collisionRadii,
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

    private async Task<SimulationScenario> CreateExtendedFlybyAsync(
        DateTime epochUtc,
        FlybySetup? flybySetup,
        CancellationToken cancellationToken)
    {
        var sunTask = _ephemerisService.GetStateAsync("10", "Солнце", epochUtc, cancellationToken);
        var mercuryTask = _ephemerisService.GetStateAsync("199", "Меркурий", epochUtc, cancellationToken);
        var venusTask = _ephemerisService.GetStateAsync("299", "Венера", epochUtc, cancellationToken);
        var earthTask = _ephemerisService.GetStateAsync("399", "Земля", epochUtc, cancellationToken);
        var marsTask = _ephemerisService.GetStateAsync("499", "Марс", epochUtc, cancellationToken);
        var jupiterTask = _ephemerisService.GetStateAsync("599", "Юпитер", epochUtc, cancellationToken);
        var saturnTask = _ephemerisService.GetStateAsync("699", "Сатурн", epochUtc, cancellationToken);
        var uranusTask = _ephemerisService.GetStateAsync("799", "Уран", epochUtc, cancellationToken);
        var neptuneTask = _ephemerisService.GetStateAsync("899", "Нептун", epochUtc, cancellationToken);

        await Task.WhenAll(
            sunTask,
            mercuryTask,
            venusTask,
            earthTask,
            marsTask,
            jupiterTask,
            saturnTask,
            uranusTask,
            neptuneTask);

        double jupiterSoiRadius = FlybyAnalysis.ComputeSphereOfInfluenceRadius(
            AstronomyConstants.JupiterMass,
            AstronomyConstants.SolarMass,
            AstronomyConstants.JupiterSemiMajorAxis);

        var bodies = new List<BodyState>
        {
            CreateBody(sunTask.Result, AstronomyConstants.SolarMass),
            CreateBody(mercuryTask.Result, AstronomyConstants.MercuryMass),
            CreateBody(venusTask.Result, AstronomyConstants.VenusMass),
            CreateBody(earthTask.Result, AstronomyConstants.EarthMass),
            CreateBody(marsTask.Result, AstronomyConstants.MarsMass),
            CreateBody(jupiterTask.Result, AstronomyConstants.JupiterMass),
            CreateBody(saturnTask.Result, AstronomyConstants.SaturnMass),
            CreateBody(uranusTask.Result, AstronomyConstants.UranusMass),
            CreateBody(neptuneTask.Result, AstronomyConstants.NeptuneMass),
        };

        var collisionRadii = new List<double>
        {
            AstronomyConstants.SolarRadius,
            AstronomyConstants.MercuryRadius,
            AstronomyConstants.VenusRadius,
            AstronomyConstants.EarthRadius,
            AstronomyConstants.MarsRadius,
            AstronomyConstants.JupiterMeanRadius,
            AstronomyConstants.SaturnMeanRadius,
            AstronomyConstants.UranusRadius,
            AstronomyConstants.NeptuneRadius,
        };

        FlybySetup setup = flybySetup ?? new FlybySetup();
        bodies.Add(CreateSpacecraft(bodies[5], bodies[0], setup, jupiterSoiRadius));
        collisionRadii.Add(0.0);

        return new SimulationScenario
        {
            Name = "Расширенная система: 8 планет + КА",
            Bodies = bodies,
            BodyCollisionRadii = collisionRadii,
            SunIndex = 0,
            JupiterIndex = 5,
            SaturnIndex = 6,
            SpacecraftIndex = bodies.Count - 1,
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
            normal = new Vector3d(0, 0, 1);

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

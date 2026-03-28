namespace PhysicsSim.Core;

public static class FlybyAnalysis
{
    /// <summary>
    /// Радиус сферы влияния по стандартной приближенной формуле Лапласа:
    /// r_soi = a * (m / M)^(2/5)
    /// Здесь a — большая полуось орбиты планеты вокруг центрального тела.
    /// </summary>
    public static double ComputeSphereOfInfluenceRadius(double planetMass, double centralMass, double semiMajorAxis)
    {
        return semiMajorAxis * Math.Pow(planetMass / centralMass, 2.0 / 5.0);
    }

    public static FlybyMetrics Compute(
        SimulationResult result,
        int sunIndex,
        int planetIndex,
        int spacecraftIndex,
        double planetSoiRadius,
        int saturnIndex = -1)
    {
        if (result.SampleCount == 0)
            throw new ArgumentException("Simulation result is empty.", nameof(result));
        if (spacecraftIndex < 0 || spacecraftIndex >= result.BodyCount)
            throw new ArgumentOutOfRangeException(nameof(spacecraftIndex));
        if (planetIndex < 0 || planetIndex >= result.BodyCount)
            throw new ArgumentOutOfRangeException(nameof(planetIndex));
        if (sunIndex < 0 || sunIndex >= result.BodyCount)
            throw new ArgumentOutOfRangeException(nameof(sunIndex));

        double minDistJ = double.PositiveInfinity;
        double minDistSaturn = double.PositiveInfinity;
        double initialDistanceToJupiter = (result.Positions[0][spacecraftIndex] - result.Positions[0][planetIndex]).Length();
        int minDistJIndex = 0;
        int entryIndex = -1;
        int exitIndex = -1;
        int equalDistanceIndex = -1;
        int collisionIndex = -1;

        bool insidePrev = false;
        for (int i = 0; i < result.SampleCount; i++)
        {
            double distJ = (result.Positions[i][spacecraftIndex] - result.Positions[i][planetIndex]).Length();
            if (distJ < minDistJ)
            {
                minDistJ = distJ;
                minDistJIndex = i;
            }

            // Вход и выход из SOI нужны, чтобы отдельно анализировать локальный
            // участок гравитационного маневра около Юпитера.
            bool inside = distJ <= planetSoiRadius;
            if (inside && !insidePrev && entryIndex < 0)
                entryIndex = i;
            if (!inside && insidePrev && entryIndex >= 0 && exitIndex < 0)
                exitIndex = i;

            // Для расширенной модели явно помечаем кадр, когда КА пересек радиус Юпитера.
            if (collisionIndex < 0 && distJ <= AstronomyConstants.JupiterMeanRadius)
                collisionIndex = i;

            // В новой метрике сравниваем скорости не "где-то потом", а при возврате
            // на то же расстояние от Юпитера, с которого начинался заход.
            if (equalDistanceIndex < 0 && i > minDistJIndex && distJ >= initialDistanceToJupiter)
                equalDistanceIndex = i;

            insidePrev = inside;

            if (saturnIndex >= 0 && saturnIndex < result.BodyCount)
            {
                // Минимальная дистанция до Сатурна полезна как отдельная метрика качества:
                // она показывает, насколько траектория вообще ведет аппарат в сторону цели.
                double distS = (result.Positions[i][spacecraftIndex] - result.Positions[i][saturnIndex]).Length();
                if (distS < minDistSaturn)
                    minDistSaturn = distS;
            }
        }

        if (entryIndex < 0 && minDistJ <= planetSoiRadius)
            entryIndex = 0;
        if (exitIndex < 0 && entryIndex >= 0)
            exitIndex = result.SampleCount - 1;

        int vInIndex = 0;
        int vOutIndex = equalDistanceIndex > vInIndex ? equalDistanceIndex : result.SampleCount - 1;
        double finalDistanceToJupiter = (result.Positions[vOutIndex][spacecraftIndex] - result.Positions[vOutIndex][planetIndex]).Length();

        // Главная скорость для оценки выигрыша — гелиоцентрическая.
        // Именно она отвечает за межпланетный "разгон" аппарата.
        double initialHeliocentricSpeed = RelativeSpeed(result, spacecraftIndex, sunIndex, vInIndex);
        double finalHeliocentricSpeed = RelativeSpeed(result, spacecraftIndex, sunIndex, vOutIndex);

        // Скорости в системе Юпитера тоже сохраняем: по ним удобно показывать,
        // что гравиманевр в системе планеты в основном поворачивает вектор скорости.
        double initialJupiterRelativeSpeed = RelativeSpeed(result, spacecraftIndex, planetIndex, vInIndex);
        double finalJupiterRelativeSpeed = RelativeSpeed(result, spacecraftIndex, planetIndex, vOutIndex);
        double deltaVGain = finalHeliocentricSpeed - initialHeliocentricSpeed;

        return new FlybyMetrics
        {
            InitialDistanceToJupiter = initialDistanceToJupiter,
            FinalDistanceToJupiter = finalDistanceToJupiter,
            JupiterSoiRadius = planetSoiRadius,
            MinDistanceToJupiter = minDistJ,
            MinDistanceToSaturn = minDistSaturn,
            ClosestApproachAltitudeToJupiter = minDistJ - AstronomyConstants.JupiterMeanRadius,
            InitialHeliocentricSpeed = initialHeliocentricSpeed,
            FinalHeliocentricSpeed = finalHeliocentricSpeed,
            InitialJupiterRelativeSpeed = initialJupiterRelativeSpeed,
            FinalJupiterRelativeSpeed = finalJupiterRelativeSpeed,
            DeltaVGainHeliocentric = deltaVGain,
            EqualDistanceIndex = equalDistanceIndex,
            EntryIndex = entryIndex,
            ExitIndex = exitIndex,
            ClosestApproachIndex = minDistJIndex,
            JupiterCollisionIndex = collisionIndex,
        };
    }

    private static double RelativeSpeed(SimulationResult result, int a, int b, int index)
    {
        return (result.Velocities[index][a] - result.Velocities[index][b]).Length();
    }
}

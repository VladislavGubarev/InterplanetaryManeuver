namespace PhysicsSim.Core;

public static class AstronomyConstants
{
    public const double AstronomicalUnit = 1.495978707e11;

    public const double SolarMass = 1.98847e30;
    public const double JupiterMass = 1.89813e27;
    public const double SaturnMass = 5.6834e26;

    public const double SolarRadius = 6.9634e8;
    public const double JupiterMeanRadius = 7.1492e7;
    public const double SaturnMeanRadius = 5.8232e7;
    public const double JupiterLowFlybyDistance = 2.0 * JupiterMeanRadius;

    public const double JupiterSemiMajorAxis = 5.2044 * AstronomicalUnit;
    public const double SaturnSemiMajorAxis = 9.5826 * AstronomicalUnit;
}

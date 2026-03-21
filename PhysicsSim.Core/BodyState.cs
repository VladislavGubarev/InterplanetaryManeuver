namespace PhysicsSim.Core;

public readonly record struct BodyState(
    string Name,
    double Mass,
    Vector3d Position,
    Vector3d Velocity);

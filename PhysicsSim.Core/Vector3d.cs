namespace PhysicsSim.Core;

public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static Vector3d Zero => new(0, 0, 0);

    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSquared() => X * X + Y * Y + Z * Z;

    public Vector3d Normalized()
    {
        double length = Length();
        if (length <= 1e-18)
            return Zero;
        return this / length;
    }

    public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vector3d Cross(Vector3d a, Vector3d b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3d operator *(double k, Vector3d v) => new(k * v.X, k * v.Y, k * v.Z);
    public static Vector3d operator *(Vector3d v, double k) => new(k * v.X, k * v.Y, k * v.Z);
    public static Vector3d operator /(Vector3d v, double k) => new(v.X / k, v.Y / k, v.Z / k);
    public static Vector3d operator -(Vector3d v) => new(-v.X, -v.Y, -v.Z);
}

namespace CIWSSim.Core.Geometry;

public struct XYZPos
{
    public double X;
    public double Y;
    public double Z;

    public XYZPos(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static XYZPos operator +(XYZPos a, XYZPos b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static XYZPos operator -(XYZPos a, XYZPos b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static XYZPos operator *(XYZPos a, double d) => new(a.X * d, a.Y * d, a.Z * d);
}

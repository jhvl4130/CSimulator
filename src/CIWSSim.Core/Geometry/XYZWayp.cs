namespace CIWSSimulator.Core.Geometry;

public struct XYZWayp
{
    public double X;
    public double Y;
    public double Z;
    public double Speed;

    public XYZWayp(double x, double y, double z, double speed)
    {
        X = x;
        Y = y;
        Z = z;
        Speed = speed;
    }
}

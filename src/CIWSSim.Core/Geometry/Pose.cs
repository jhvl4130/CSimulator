namespace CIWSSimulator.Core.Geometry;

public struct Pose
{
    public double Yaw;
    public double Pitch;
    public double Roll;

    public Pose(double yaw, double pitch, double roll)
    {
        Yaw = yaw;
        Pitch = pitch;
        Roll = roll;
    }
}

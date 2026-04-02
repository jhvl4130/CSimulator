using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Models;

/// <summary>
/// 외부에서 전달받는 탄환 궤적 데이터 한 점.
/// </summary>
public readonly struct BulletPoint
{
    public readonly double Time;
    public readonly XYZPos Pos;

    public BulletPoint(double time, XYZPos pos)
    {
        Time = time;
        Pos = pos;
    }

    public BulletPoint(double time, double x, double y, double z)
    {
        Time = time;
        Pos = new XYZPos(x, y, z);
    }
}

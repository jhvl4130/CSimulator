using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Core.Events;

/// <summary>Bullet → FCS: 탄 위치/속도 보고</summary>
public class BulletPositionEvent : SimEvent
{
    public XYZPos Pos { get; }
    public XYZPos Vel { get; }

    public BulletPositionEvent(XYZPos pos, XYZPos vel)
    {
        Pos = pos;
        Vel = vel;
    }
}

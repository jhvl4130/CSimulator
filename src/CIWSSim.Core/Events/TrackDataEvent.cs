using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Core.Events;

/// <summary>TrackRadar → FCS: 추적 데이터 (위치/속도/가속도).</summary>
public class TrackDataEvent : SimEvent
{
    public int TargetId { get; }
    public XYZPos Pos { get; }
    public XYZPos Vel { get; }
    public XYZPos Acc { get; }

    public TrackDataEvent(int targetId, XYZPos pos, XYZPos vel, XYZPos acc)
    {
        TargetId = targetId;
        Pos = pos;
        Vel = vel;
        Acc = acc;
    }
}

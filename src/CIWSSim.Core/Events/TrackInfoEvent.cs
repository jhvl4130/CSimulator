using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Core.Events;

/// <summary>
/// SearchRadar → C2 / TrackRadar → FCS: 추적 정보 (위치/속도/가속도).
/// </summary>
public class TrackInfoEvent : SimEvent
{
    public int TargetId { get; }
    public XYZPos Pos { get; }
    public XYZPos Vel { get; }
    public XYZPos Acc { get; }

    public TrackInfoEvent(int targetId, XYZPos pos, XYZPos vel, XYZPos acc)
    {
        TargetId = targetId;
        Pos = pos;
        Vel = vel;
        Acc = acc;
    }
}

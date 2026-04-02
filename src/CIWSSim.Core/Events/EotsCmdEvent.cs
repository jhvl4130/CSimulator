using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Core.Events;

/// <summary>FCS → EOTS: 종속 추적 명령 (표적 위치 포함).</summary>
public class EotsCmdEvent : SimEvent
{
    public XYZPos TargetPos { get; }

    public EotsCmdEvent(XYZPos targetPos)
    {
        TargetPos = targetPos;
    }
}

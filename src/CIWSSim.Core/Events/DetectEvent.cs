using CIWSSim.Core.Geometry;

namespace CIWSSim.Core.Events;

/// <summary>SearchRadar → C2Control: 표적 탐지 보고.</summary>
public class DetectEvent : SimEvent
{
    public Model Target { get; }
    public XYZPos Pos { get; }
    public double Speed { get; }

    public DetectEvent(Model target, XYZPos pos, double speed)
    {
        Target = target;
        Pos = pos;
        Speed = speed;
    }
}

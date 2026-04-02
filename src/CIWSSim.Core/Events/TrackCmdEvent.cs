namespace CIWSSimulator.Core.Events;

/// <summary>FCS → TrackRadar: 표적 추적 명령.</summary>
public class TrackCmdEvent : SimEvent
{
    public Model Target { get; }

    public TrackCmdEvent(Model target)
    {
        Target = target;
    }
}

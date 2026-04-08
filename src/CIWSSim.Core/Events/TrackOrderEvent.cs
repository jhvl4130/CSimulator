namespace CIWSSimulator.Core.Events;

public enum TrackOrderCmd
{
    Start,
    Stop
}

/// <summary>FCS → TrackRadar: 추적 명령 (시작/중지 + 주기 설정).</summary>
public class TrackOrderEvent : SimEvent
{
    public int TargetId { get; }
    public TrackOrderCmd Cmd { get; }
    public double Period { get; }
    public Model? Target { get; }

    public TrackOrderEvent(int targetId, TrackOrderCmd cmd, double period, Model? target = null)
    {
        TargetId = targetId;
        Cmd = cmd;
        Period = period;
        Target = target;
    }
}

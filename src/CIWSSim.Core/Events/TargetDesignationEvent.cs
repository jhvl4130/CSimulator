namespace CIWSSimulator.Core.Events;

public enum DesignationCmd
{
    Start,
    Stop
}

/// <summary>C2Control → FCS: 표적 지정 (교전 시작/중지).</summary>
public class TargetDesignationEvent : SimEvent
{
    public DesignationCmd Cmd { get; }
    public int TargetId { get; }
    public Model? Target { get; }

    public TargetDesignationEvent(DesignationCmd cmd, int targetId, Model? target = null)
    {
        Cmd = cmd;
        TargetId = targetId;
        Target = target;
    }
}

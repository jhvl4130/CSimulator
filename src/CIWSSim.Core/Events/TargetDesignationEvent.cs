namespace CIWSSimulator.Core.Events;

/// <summary>C2Control → FCS: 표적 지정 (교전 시작/중지).</summary>
public class TargetDesignationEvent : SimEvent
{
    public string Cmd { get; }
    public int TargetId { get; }
    public Model? Target { get; }

    public TargetDesignationEvent(string cmd, int targetId, Model? target = null)
    {
        Cmd = cmd;
        TargetId = targetId;
        Target = target;
    }
}

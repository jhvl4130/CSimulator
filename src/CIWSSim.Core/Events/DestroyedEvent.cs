namespace CIWSSimulator.Core.Events;

/// <summary>
/// Target → FCS: 표적 파괴 통보
/// </summary>
public class DestroyedEvent : SimEvent
{
    public int TargetId { get; }

    public DestroyedEvent(int targetId)
    {
        TargetId = targetId;
    }
}

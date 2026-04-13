namespace CIWSSimulator.Core.Events;

/// <summary>
/// TrackRadar → FCS: 추적 상실
/// </summary>
public class TrackLostEvent : SimEvent
{
    public int TargetId { get; }

    public TrackLostEvent(int targetId)
    {
        TargetId = targetId;
    }
}

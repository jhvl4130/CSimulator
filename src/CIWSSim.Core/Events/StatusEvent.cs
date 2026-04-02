namespace CIWSSimulator.Core.Events;

/// <summary>FCS → C2Control, Gun → FCS: 상태 보고.</summary>
public class StatusEvent : SimEvent
{
    public string EventType { get; }
    public string Description { get; }

    public StatusEvent(string eventType, string description)
    {
        EventType = eventType;
        Description = description;
    }
}

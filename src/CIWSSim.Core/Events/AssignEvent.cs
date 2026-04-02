namespace CIWSSimulator.Core.Events;

/// <summary>C2Control → FCS: 표적 할당.</summary>
public class AssignEvent : SimEvent
{
    public Model Target { get; }
    public int Priority { get; }

    public AssignEvent(Model target, int priority = 0)
    {
        Target = target;
        Priority = priority;
    }
}

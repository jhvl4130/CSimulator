namespace CIWSSimulator.Core.Events;

/// <summary>AssetZone → C2 → FCS: 요격 실패 (표적이 방어존 도달)</summary>
public class FailEvent : SimEvent
{
    public Model Target { get; }

    public FailEvent(Model target)
    {
        Target = target;
    }
}

namespace CIWSSimulator.Core.Events;

/// <summary>다방향 상태 보고: 모델 → 상위 모델.</summary>
public class HealthEvent : SimEvent
{
    public int Id { get; }
    public double Health { get; }

    public HealthEvent(int id, double health)
    {
        Id = id;
        Health = health;
    }
}

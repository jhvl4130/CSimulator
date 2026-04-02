namespace CIWSSim.Core.Events;

/// <summary>Gun → FCS: 탄환 명중 결과.</summary>
public class HitResultEvent : SimEvent
{
    public int TargetId { get; }
    public bool IsHit { get; }
    public double Damage { get; }

    public HitResultEvent(int targetId, bool isHit, double damage)
    {
        TargetId = targetId;
        IsHit = isHit;
        Damage = damage;
    }
}

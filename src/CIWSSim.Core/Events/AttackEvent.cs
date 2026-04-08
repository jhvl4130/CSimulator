namespace CIWSSimulator.Core.Events;

/// <summary>Bullet → Target: 공격 (피해 전달).</summary>
public class AttackEvent : SimEvent
{
    public double Power { get; }
    public Model? SourceFcs { get; }

    public AttackEvent(double power, Model? sourceFcs = null)
    {
        Power = power;
        SourceFcs = sourceFcs;
    }
}

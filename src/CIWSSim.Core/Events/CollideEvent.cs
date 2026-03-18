namespace CIWSSim.Core.Events;

public class CollideEvent : SimEvent
{
    public double Power { get; }

    public CollideEvent(double power)
    {
        Power = power;
    }
}

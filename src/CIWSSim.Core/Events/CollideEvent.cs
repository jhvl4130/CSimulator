namespace CIWSSim.Core.Events;

public class CollideEvent : SimEvent
{
    public double Power { get; set; }

    public CollideEvent(double power)
    {
        Ev = EvCollide;
        Power = power;
    }
}

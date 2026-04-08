namespace CIWSSimulator.Core.Events;

public enum FireCmd
{
    On,
    Off
}

/// <summary>FCS → Gun: 사격 명령 (on/off).</summary>
public class FireEvent : SimEvent
{
    public FireCmd Cmd { get; }

    public FireEvent(FireCmd cmd)
    {
        Cmd = cmd;
    }
}

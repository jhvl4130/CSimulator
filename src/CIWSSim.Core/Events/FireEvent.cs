namespace CIWSSimulator.Core.Events;

/// <summary>FCS → Gun: 사격 명령 (on/off).</summary>
public class FireEvent : SimEvent
{
    public string Cmd { get; }

    public FireEvent(string cmd)
    {
        Cmd = cmd;
    }
}

namespace CIWSSimulator.Core.Events;

public enum FireCmd
{
    On,
    Off
}

/// <summary>
/// FCS → Gun: 사격 명령 (on/off)
/// </summary>
public class FireEvent : SimEvent
{
    public FireCmd Cmd { get; }

    // 260415 사격 대상 InputId (Off면 0)
    public int TargetId { get; }

    // 사격 대상 Type (Off면 0)
    public int TargetType { get; }

    public FireEvent(FireCmd cmd, int targetId = 0, int targetType = 0)
    {
        Cmd = cmd;
        TargetId = targetId;
        TargetType = targetType;
    }
}

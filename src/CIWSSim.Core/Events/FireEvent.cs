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

    // 260415 사격 대상 Tag (Off면 빈 문자열)
    public string TargetTag { get; }

    public FireEvent(FireCmd cmd, int targetId = 0, string targetTag = "")
    {
        Cmd = cmd;
        TargetId = targetId;
        TargetTag = targetTag;
    }
}

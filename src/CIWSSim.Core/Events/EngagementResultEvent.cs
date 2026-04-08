namespace CIWSSimulator.Core.Events;

public enum EngagementStatus
{
    Engaging,
    FireStart,
    Success,
    Fail
}

/// <summary>
/// FCS → C2: 교전 결과 보고.
/// 교전 중 주기적으로 + 종료 시 전송.
/// </summary>
public class EngagementResultEvent : SimEvent
{
    public int TargetId { get; }
    public double Azimuth { get; }
    public double Elevation { get; }
    public int BulletFire { get; }
    public int BulletRemain { get; }
    public EngagementStatus Status { get; }

    public EngagementResultEvent(int targetId,
        double azimuth, double elevation,
        int bulletFire, int bulletRemain, EngagementStatus status)
    {
        TargetId = targetId;
        Azimuth = azimuth;
        Elevation = elevation;
        BulletFire = bulletFire;
        BulletRemain = bulletRemain;
        Status = status;
    }
}

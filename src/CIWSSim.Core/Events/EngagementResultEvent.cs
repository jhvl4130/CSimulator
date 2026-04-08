namespace CIWSSimulator.Core.Events;

/// <summary>
/// FCS → C2: 교전 결과 보고.
/// 교전 중 주기적으로 + 종료 시 전송.
/// Status: "engaging", "success", "fail"
/// </summary>
public class EngagementResultEvent : SimEvent
{
    public int TargetId { get; }
    public double Azimuth { get; }
    public double Elevation { get; }
    public int BulletFire { get; }
    public int BulletRemain { get; }
    public string Status { get; }

    public EngagementResultEvent(int targetId,
        double azimuth, double elevation,
        int bulletFire, int bulletRemain, string status)
    {
        TargetId = targetId;
        Azimuth = azimuth;
        Elevation = elevation;
        BulletFire = bulletFire;
        BulletRemain = bulletRemain;
        Status = status;
    }
}

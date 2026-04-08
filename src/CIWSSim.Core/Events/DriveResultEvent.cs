namespace CIWSSimulator.Core.Events;

/// <summary>
/// Gun → FCS: 구동 결과 (200Hz 주기적 보고).
/// 실제 조준 방위각/고각, 각속도, 발사수, 잔탄, 사격 상태.
/// </summary>
public class DriveResultEvent : SimEvent
{
    public double Azimuth { get; }
    public double Elevation { get; }
    public double AzimVel { get; }
    public double ElevVel { get; }
    public int BulletFire { get; }
    public int BulletRemain { get; }
    public string FireStatus { get; }

    public DriveResultEvent(double azimuth, double elevation,
        double azimVel, double elevVel,
        int bulletFire, int bulletRemain, string fireStatus)
    {
        Azimuth = azimuth;
        Elevation = elevation;
        AzimVel = azimVel;
        ElevVel = elevVel;
        BulletFire = bulletFire;
        BulletRemain = bulletRemain;
        FireStatus = fireStatus;
    }
}

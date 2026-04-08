namespace CIWSSimulator.Core.Events;

public enum FireStatus
{
    Idle,
    Firing,
    Slewing,
    AmmoOut
}

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
    public FireStatus FireStatus { get; }

    public DriveResultEvent(double azimuth, double elevation,
        double azimVel, double elevVel,
        int bulletFire, int bulletRemain, FireStatus fireStatus)
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

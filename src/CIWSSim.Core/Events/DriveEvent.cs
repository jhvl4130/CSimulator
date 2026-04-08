namespace CIWSSimulator.Core.Events;

/// <summary>FCS → Gun: 조준 구동 명령 (방위각/고각).</summary>
public class DriveEvent : SimEvent
{
    public double Azimuth { get; }
    public double Elevation { get; }

    public DriveEvent(double azimuth, double elevation)
    {
        Azimuth = azimuth;
        Elevation = elevation;
    }
}

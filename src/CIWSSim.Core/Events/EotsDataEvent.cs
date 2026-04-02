namespace CIWSSimulator.Core.Events;

/// <summary>EOTS → FCS: 정밀 추적 결과 (고각/방위각).</summary>
public class EotsDataEvent : SimEvent
{
    public double Azimuth { get; }
    public double Elevation { get; }
    public bool IsTracking { get; }

    public EotsDataEvent(double azimuth, double elevation, bool isTracking)
    {
        Azimuth = azimuth;
        Elevation = elevation;
        IsTracking = isTracking;
    }
}

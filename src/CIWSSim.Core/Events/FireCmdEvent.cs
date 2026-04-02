using CIWSSim.Core.Geometry;

namespace CIWSSim.Core.Events;

/// <summary>FCS → Gun: 사격 명령.</summary>
public class FireCmdEvent : SimEvent
{
    public double AimAzimuth { get; }
    public double AimElevation { get; }
    public Model Target { get; }

    public FireCmdEvent(double aimAzimuth, double aimElevation, Model target)
    {
        AimAzimuth = aimAzimuth;
        AimElevation = aimElevation;
        Target = target;
    }
}

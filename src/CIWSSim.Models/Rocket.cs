using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

public class Rocket : TargetBase
{
    public Rocket(int id, XYZPos lp, double speed, double azi, double ele) : base(id)
    {
        Type = MtRocket;
        Name = $"Rocket-{id}";

        IniPos = lp;
        IniSpeed = speed;
        IniAzimuth = azi;
        IniElevation = ele;

        Power = 50.0;
    }

    public override double Init(double t)
    {
        InitRuntimeVars();

        Phase = PhaseType.Run;
        IsEnabled = true;
        Speed = IniSpeed;

        Logger.Dbg(DbgFlag.Init, $"{t:F6} [{Name}] created\n");

        return MovePeriod;
    }

    public override double IntTrans(double t)
    {
        double tN = TInfinite;

        switch (Phase)
        {
            case PhaseType.Run:
                tN = MovePeriod;

                // 260415
                CheckAssetZoneCollision(t);
                if (CheckDestinationReached(t))
                {
                    tN = TInfinite;
                }
                break;
        }

        return tN;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        switch (ev)
        {
            case AttackEvent attack:
                HandleAttack(t, attack);
                break;

            case CollideEvent:
                HandleCollide(t);
                break;
        }
        return TContinue;
    }
}

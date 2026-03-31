using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

public class Rocket : Model
{
    public XYZPos Gip { get; set; }

    public Rocket(int id, XYZPos lp, XYZPos gip, double speed, double azi, double ele) : base(id)
    {
        Class = ModelClass.Target;
        Type = MtRocket;
        Name = $"Rocket-{id}";

        IniPos = lp;
        Gip = gip;
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
                // move (placeholder - same as C++ original)

                // 충돌 판정
                foreach (var target in Engine!.GetCollidables())
                {
                    if (!target.IsEnabled) continue;
                    if (CollisionDetection.IsCollide(Pos, target.Building))
                    {
                        Logger.Dbg(DbgFlag.Collide,
                            $"{t:F6} [{Name}] ↔ [{target.Name}] Collide\n");
                        Engine.SendEvent(target, new CollideEvent(Power));
                        Phase = PhaseType.End;
                        IsEnabled = false;
                        tN = TInfinite;
                        break;
                    }
                }
                break;
        }

        return tN;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        if (ev is CollideEvent)
        {
            Phase = PhaseType.End;
            IsEnabled = false;
            Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] destroyed by defense zone\n");
        }
        return TContinue;
    }
}

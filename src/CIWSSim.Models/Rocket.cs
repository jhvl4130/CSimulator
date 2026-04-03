using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

public class Rocket : Model
{
    public Rocket(int id, XYZPos lp, double speed, double azi, double ele) : base(id)
    {
        Class = ModelClass.Target;
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

                // 충돌 판정
                foreach (var target in Engine!.GetModelsByClass(ModelClass.Asset))
                {
                    if (!target.IsEnabled) continue;
                    if (CollisionDetection.IsCollide(Pos, target.Building))
                    {
                        Logger.Dbg(DbgFlag.Collide,
                            $"{t:F6} [{Name}] ↔ [{target.Name}] Collide\n");
                        Engine.SendEvent(target, new CollideEvent(Power));
                        EndTarget();
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
            Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] destroyed by defense zone\n");
            EndTarget();
        }
        return TContinue;
    }

    private void EndTarget()
    {
        Phase = PhaseType.End;
        IsEnabled = false;
        Engine?.RemoveModel(Id);
    }
}

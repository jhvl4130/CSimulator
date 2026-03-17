using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

public class Airplane : Model
{
    public Airplane(int id) : base(id)
    {
        Class = ClsPlatform;
        Type = MtAirplane;
        Name = $"Airplane-{id}";
        Power = 50.0;
    }

    public override double Init(double t)
    {
        InitRuntimeVars();

        double tN = TInfinite;

        if (StartT > 0.0)
        {
            Phase = PhaseWaitStart;
            IsEnabled = false;
            Speed = 0.0;
            tN = StartT;
        }
        else
        {
            Phase = PhaseRun;
            IsEnabled = true;
            Speed = IniSpeed;
            tN = MovePeriod;
        }

        return tN;
    }

    public override double IntTrans(double t)
    {
        double tN = TInfinite;

        switch (Phase)
        {
            case PhaseWaitStart:
                Phase = PhaseRun;
                tN = MovePeriod;
                break;

            case PhaseRun:
            {
                tN = MovePeriod;
                // move
                var pos = Pos;
                pos.X += Speed * MovePeriod;
                Pos = pos;

                Logger.Dbg(DbgFlag.Move,
                    $"{t:F6} [{Name}] x={Pos.X:F6} y={Pos.Y:F6} z={Pos.Z:F6} speed={Speed:F6}\n");

                // check collision
                var assets = Engine!.GetAssets();
                var ev = new CollideEvent(Power);

                foreach (var asset in assets)
                {
                    if (asset.IsEnabled && CollisionDetection.IsCollide(in pos, asset.Building))
                    {
                        Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] Collide\n");
                        IsEnabled = false;
                        tN = TInfinite;
                        Engine.SendEvent(asset, ev);
                    }
                }
                break;
            }
        }

        return tN;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }
}

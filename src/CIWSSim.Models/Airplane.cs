using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

public class Airplane : Model
{
    private readonly WaypointMover _mover = new();

    public Airplane(int id) : base(id)
    {
        Class = ModelClass.Platform;
        Type = MtAirplane;
        Name = $"Airplane-{id}";
        Power = 50.0;
    }

    /// <summary>WaypointMover 설정 접근용.</summary>
    public WaypointMover Mover => _mover;

    public override double Init(double t)
    {
        InitRuntimeVars();
        _mover.Reset();

        double tN = TInfinite;

        if (StartT > 0.0)
        {
            Phase = PhaseType.WaitStart;
            IsEnabled = false;
            Speed = 0.0;
            tN = StartT;
        }
        else
        {
            Phase = PhaseType.Run;
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
            case PhaseType.WaitStart:
                Phase = PhaseType.Run;
                IsEnabled = true;
                Speed = IniSpeed;
                tN = MovePeriod;
                break;

            case PhaseType.Run:
            {
                tN = MovePeriod;

                bool reached = _mover.Step(this, MovePeriod);

                if (reached && _mover.IsFinished(this))
                {
                    Phase = PhaseType.End;
                    IsEnabled = false;
                    Logger.Dbg(DbgFlag.Move, $"[{Name}] 마지막 웨이포인트 도달\n");
                    tN = TInfinite;
                    break;
                }

                Logger.Dbg(DbgFlag.Move,
                    $"{t:F6} [{Name}] x={Pos.X:F2} y={Pos.Y:F2} z={Pos.Z:F2} " +
                    $"yaw={Pose.Yaw:F2} pitch={Pose.Pitch:F2} speed={Speed:F2}\n");

                // 충돌 판정
                var pos = Pos;
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

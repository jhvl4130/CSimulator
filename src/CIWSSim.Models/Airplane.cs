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
        Class = ModelClass.Target;
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
                if (!IsEnabled) break;

                Logger.Dbg(DbgFlag.Move,
                    $"{t:F6} [{Name}] x={Pos.X:F2} y={Pos.Y:F2} z={Pos.Z:F2} " +
                    $"yaw={Pose.Yaw:F2} pitch={Pose.Pitch:F2} speed={Speed:F2}\n");
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

using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

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
                    Logger.Dbg(DbgFlag.Move, $"[{Name}] 마지막 웨이포인트 도달\n");
                    EndTarget();
                    tN = TInfinite;
                    break;
                }

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
        switch (ev)
        {
            case AttackEvent attack:
                Health -= attack.Power;
                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] Attacked, health={Health:F1}\n");
                if (Health <= 0.0)
                {
                    // FCS에 파괴 통보
                    if (attack.SourceFcs is not null)
                    {
                        Engine!.SendEvent(attack.SourceFcs, new DestroyedEvent(Id));
                    }
                    EndTarget();
                }
                break;

            case CollideEvent:
                Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] destroyed by defense zone\n");
                EndTarget();
                break;
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

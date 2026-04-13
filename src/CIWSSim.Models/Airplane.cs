using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

public class Airplane : TargetBase
{
    private readonly WaypointMover _mover = new();

    public Airplane(int id) : base(id)
    {
        Type = MtAirplane;
        Name = $"Airplane-{id}";
        Power = 50.0;
    }

    /// <summary>
    /// WaypointMover 설정 접근용
    /// </summary>
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
                IsStateChanged = true;

                if (reached && _mover.IsFinished(this))
                {
                    Logger.Dbg(DbgFlag.Move, $"[{Name}] 마지막 웨이포인트 도달\n");
                    EndTarget();
                    tN = TInfinite;
                    break;
                }

                if (CheckAssetZoneCollision(t))
                {
                    tN = TInfinite;
                    break;
                }

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
                HandleAttack(t, attack);
                break;

            case CollideEvent:
                HandleCollide(t);
                break;
        }
        return TContinue;
    }
}

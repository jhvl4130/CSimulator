using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

public class Uav : TargetBase
{
    private readonly WaypointMover _mover = new();

    public Uav(int id) : base(id)
    {
        Type = MtUav;
        Name = $"Uav-{id}";
        Power = 50.0;
        _mover.PoseTurnRate = 30.0;
        _mover.AltRate = 40.0;
    }

    public WaypointMover Mover => _mover;

    public override double Init(double t)
    {
        InitRuntimeVars();
        _mover.Reset();

        if (StartT > 0.0)
        {
            Phase = PhaseType.WaitStart;
            IsEnabled = false;
            Speed = 0.0;
            return StartT;
        }

        Phase = PhaseType.Run;
        IsEnabled = true;
        Speed = IniSpeed;
        return MovePeriod;
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
                    EndTarget();
                    tN = TInfinite;
                    break;
                }

                // 260415 AssetZone 진입 = 상태 전환만
                CheckAssetZoneCollision(t);
                // Test 목표 지점 통과 시 소멸
                if (CheckDestinationReached(t))
                {
                    tN = TInfinite;
                    break;
                }
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

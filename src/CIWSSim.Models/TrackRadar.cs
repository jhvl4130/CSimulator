using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

/// <summary>
/// 추적 레이더. FCS의 명령으로 표적을 추적하며,
/// 주기적으로 위치/속도/가속도를 FCS에 전달한다.
/// </summary>
public class TrackRadar : Model
{
    /// <summary>추적 주기 (초). 예: 0.04 = 25Hz, 0.02 = 50Hz, 0.01 = 100Hz.</summary>
    public double TrackPeriod { get; set; } = 0.04;

    /// <summary>FCS 참조 (생성 시 주입).</summary>
    public Model? Fcs { get; set; }

    /// <summary>현재 추적 중인 표적.</summary>
    private Model? _target;

    /// <summary>이전 추적 위치 (속도 계산용).</summary>
    private XYZPos _prevPos;
    private XYZPos _prevVel;
    private double _prevT;
    private bool _hasPrev;

    public TrackRadar(int id) : base(id)
    {
        Class = ModelClass.Sensor;
        Type = MtTRadar;
        Name = $"TrackRadar-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.WaitStart;
        IsEnabled = true;
        _hasPrev = false;
        return TInfinite;
    }

    public override double IntTrans(double t)
    {
        if (Phase != PhaseType.Run || !IsEnabled || _target is null)
            return TInfinite;

        // 표적 추적 상실 체크
        if (!_target.IsEnabled)
        {
            Logger.Dbg(DbgFlag.Collide,
                $"{t:F6} [{Name}] Track lost: [{_target.Name}] destroyed\n");
            if (Fcs is not null)
                Engine!.SendEvent(Fcs, new TrackLostEvent(_target.Id));
            StopTracking();
            return TInfinite;
        }

        // 위치/속도/가속도 계산
        var pos = _target.Pos;
        XYZPos vel, acc;

        if (_hasPrev && t > _prevT)
        {
            double dt = t - _prevT;
            vel = new XYZPos(
                (pos.X - _prevPos.X) / dt,
                (pos.Y - _prevPos.Y) / dt,
                (pos.Z - _prevPos.Z) / dt);
            acc = new XYZPos(
                (vel.X - _prevVel.X) / dt,
                (vel.Y - _prevVel.Y) / dt,
                (vel.Z - _prevVel.Z) / dt);
        }
        else
        {
            vel = new XYZPos(0, 0, 0);
            acc = new XYZPos(0, 0, 0);
        }

        _prevPos = pos;
        _prevVel = vel;
        _prevT = t;
        _hasPrev = true;

        // FCS에 추적 데이터 전송
        if (Fcs is not null)
        {
            Engine!.SendEvent(Fcs, new TrackDataEvent(_target.Id, pos, vel, acc));
        }

        return TrackPeriod;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        if (ev is TrackCmdEvent cmd)
        {
            _target = cmd.Target;
            _hasPrev = false;
            Phase = PhaseType.Run;
            Logger.Dbg(DbgFlag.Init,
                $"{t:F6} [{Name}] Tracking [{_target.Name}]\n");
            return TrackPeriod;
        }

        return TContinue;
    }

    private void StopTracking()
    {
        _target = null;
        _hasPrev = false;
        Phase = PhaseType.WaitStart;
    }
}

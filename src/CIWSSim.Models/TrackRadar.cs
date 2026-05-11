using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 추적 레이더. FCS의 명령으로 표적을 추적하며,
/// 주기적으로 위치/속도/가속도를 FCS에 전달한다.
/// </summary>
public class TrackRadar : Model
{
    /// <summary>
    /// 추적 주기 (초) 예: 0.04 = 25Hz, 0.02 = 50Hz, 0.01 = 100Hz
    /// </summary>
    public double TrackPeriod { get; set; } = 0.04;

    /// <summary>
    /// FCS 참조 (생성 시 주입)
    /// </summary>
    public Model? Fcs { get; set; }

    /// <summary>지형 LOS 게이트 (null이면 LOS 검사 생략)</summary>
    public TerrainMap? Terrain { get; set; }

    /// <summary>LOS 광선 샘플 간격(m)</summary>
    public double SampleStepM { get; set; } = 30.0;

    /// <summary>4/3 등가지구반경 보정 사용 여부</summary>
    public bool UseEarthCurvature { get; set; } = true;

    /// <summary>안테나 높이(m). Pos.Z에 가산해서 LOS 시작점으로 사용.</summary>
    public double AntennaHeightM { get; set; } = 0.0;

    /// <summary>연속 LOS 실패 tick 수가 이 값에 도달하면 TrackLost</summary>
    public int LosLossTicks { get; set; } = 3;

    /// <summary>
    /// 현재 추적 중인 표적
    /// </summary>
    private Model? _target;

    /// <summary>
    /// 이전 추적 위치 (속도 계산용)
    /// </summary>
    private XYZPos _prevPos;
    private XYZPos _prevVel;
    private double _prevT;
    private bool _hasPrev;

    /// <summary>연속 LOS 실패 tick 카운터</summary>
    private int _losMissCount;

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

        // 지형 LOS 게이트 (hysteresis: 연속 LosLossTicks tick 실패 시 TrackLost)
        if (Terrain is not null)
        {
            var from = new XYZPos(Pos.X, Pos.Y, Pos.Z + AntennaHeightM);
            bool los = Terrain.HasLineOfSight(from, _target.Pos, SampleStepM, UseEarthCurvature);
            if (!los)
            {
                _losMissCount++;
                if (_losMissCount >= LosLossTicks)
                {
                    Logger.Dbg(DbgFlag.Collide,
                        $"{t:F6} [{Name}] Track lost: [{_target.Name}] LOS blocked {_losMissCount} ticks\n");
                    if (Fcs is not null)
                        Engine!.SendEvent(Fcs, new TrackLostEvent(_target.Id));
                    StopTracking();
                    return TInfinite;
                }
                // hysteresis 임계 미달 - 이번 tick은 FCS에 데이터 안 보냄
                return TrackPeriod;
            }
            _losMissCount = 0;
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
            Engine!.SendEvent(Fcs, new TrackInfoEvent(_target.Id, pos, vel, acc));
        }

        return TrackPeriod;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        if (ev is TrackOrderEvent order)
        {
            if (order.Cmd == TrackOrderCmd.Start && order.Target is not null)
            {
                _target = order.Target;
                TrackPeriod = order.Period;
                _hasPrev = false;
                _losMissCount = 0;
                Phase = PhaseType.Run;
                Logger.Dbg(DbgFlag.Init,
                    $"{t:F6} [{Name}] Tracking [{_target.Name}] period={TrackPeriod}s\n");
                return TrackPeriod;
            }
            else if (order.Cmd == TrackOrderCmd.Stop)
            {
                Logger.Dbg(DbgFlag.Init,
                    $"{t:F6} [{Name}] Track stop ordered\n");
                StopTracking();
                return TInfinite;
            }
        }

        return TContinue;
    }

    private void StopTracking()
    {
        _target = null;
        _hasPrev = false;
        _losMissCount = 0;
        Phase = PhaseType.WaitStart;
    }
}

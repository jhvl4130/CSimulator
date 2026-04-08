using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 사격통제 시스템 (FCS). CIWS당 1개.
/// TrackRadar에 추적 명령, Gun에 구동/사격 명령을 내리고
/// 피해평가(PHP)를 수행한다.
/// </summary>
public class FCS : Model
{
    /// <summary>사격 가능 거리 (m).</summary>
    public double FireRange { get; set; } = 1500.0;

    /// <summary>소속 CIWS ID.</summary>
    public int CiwsId { get; set; }

    /// <summary>PHP 오차 모델.</summary>
    public double PhpErr { get; set; }

    /// <summary>추적 주기 (TrackRadar에 전달).</summary>
    public double TrackPeriod { get; set; } = 0.04;

    /// <summary>교전 결과 보고 주기 (초).</summary>
    public double EngagementReportPeriod { get; set; } = 1.0;

    // ── 참조 (생성 시 주입) ──
    public Model? TrackRadar { get; set; }
    public Model? GunModel { get; set; }
    public Model? C2 { get; set; }

    // ── 교전 대상 ──
    private Model? _target;

    // ── 최신 추적 데이터 ──
    private XYZPos _trackPos;
    private XYZPos _trackVel;
    private double _aimAzimuth;
    private double _aimElevation;
    private double _trackDist;

    // ── Gun 피드백 (DriveResult) ──
    private int _bulletFire;
    private int _bulletRemain;

    // ── 교전 통계 (PHP 계산용) ──
    private int _firedCount;
    private int _hitCount;

    public FCS(int id) : base(id)
    {
        Class = ModelClass.C2;
        Type = MtFcs;
        Name = $"FCS-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.Wait;
        IsEnabled = true;
        return TInfinite;
    }

    public override double IntTrans(double t)
    {
        // 교전 중 주기적 EngagementResult 보고
        if (Phase == PhaseType.FireOn || Phase == PhaseType.TrackRcvd
            || Phase == PhaseType.StartEngage)
        {
            SendEngagementResult("engaging");
            return EngagementReportPeriod;
        }

        return TInfinite;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        switch (ev)
        {
            case TargetDesignationEvent desig:
                return HandleTargetDesignation(t, desig);

            case TrackInfoEvent trackInfo:
                return HandleTrackInfo(t, trackInfo);

            case DriveResultEvent driveResult:
                return HandleDriveResult(t, driveResult);

            case BulletPositionEvent bulletPos:
                return HandleBulletPosition(t, bulletPos);

            case DestroyedEvent destroyed:
                return HandleDestroyed(t, destroyed);

            case FailEvent fail:
                return HandleFail(t, fail);

            case TrackLostEvent trackLost:
                return HandleTrackLost(t, trackLost);

            case CollideEvent collide:
                return HandleCollide(t, collide);

            case HealthEvent health:
                return TContinue;
        }

        return TContinue;
    }

    // ── ⓘ TargetDesignation (C2 → FCS) ──

    private double HandleTargetDesignation(double t, TargetDesignationEvent ev)
    {
        if (ev.Cmd == "start")
        {
            if (Phase != PhaseType.Wait)
            {
                Logger.Warn($"{t:F6} [{Name}] Designation rejected: busy (phase={Phase})\n");
                return TContinue;
            }

            _target = ev.Target;
            Phase = PhaseType.StartEngage;
            ResetStats();

            Logger.Dbg(DbgFlag.Init,
                $"{t:F6} [{Name}] Designated target {ev.TargetId}\n");

            // TrackRadar에 추적 명령
            if (TrackRadar is not null)
            {
                Engine!.SendEvent(TrackRadar,
                    new TrackOrderEvent(ev.TargetId, "start", TrackPeriod, _target));
            }

            return EngagementReportPeriod;
        }
        else if (ev.Cmd == "stop")
        {
            Logger.Dbg(DbgFlag.Init,
                $"{t:F6} [{Name}] Designation stop for target {ev.TargetId}\n");
            EndEngagement(t, "fail");
            return TInfinite;
        }

        return TContinue;
    }

    // ── ⓘ TrackInfo (TrackRadar → FCS) ──

    private double HandleTrackInfo(double t, TrackInfoEvent ev)
    {
        if (Phase != PhaseType.StartEngage && Phase != PhaseType.TrackRcvd
            && Phase != PhaseType.FireOn)
            return TContinue;

        _trackPos = ev.Pos;
        _trackVel = ev.Vel;

        if (_target is null || !_target.IsEnabled)
            return TContinue;

        // 방위각/고각/거리 계산
        _aimAzimuth = GeoUtil.Bearing(Pos, _trackPos);
        double dx = _trackPos.X - Pos.X;
        double dy = _trackPos.Y - Pos.Y;
        double dz = _trackPos.Z - Pos.Z;
        double dist2D = Math.Sqrt(dx * dx + dy * dy);
        _aimElevation = GeoUtil.RadToDeg(Math.Atan2(dz, dist2D));
        _trackDist = GeoUtil.Distance(Pos, _trackPos);

        if (Phase == PhaseType.StartEngage || Phase == PhaseType.TrackRcvd)
        {
            Phase = PhaseType.TrackRcvd;

            if (_trackDist <= FireRange)
            {
                Phase = PhaseType.FireOn;
                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] FireOn [{_target.Name}] dist={_trackDist:F1}m\n");

                // Gun에 조준 + 사격 명령
                if (GunModel is not null)
                {
                    Engine!.SendEvent(GunModel, new DriveEvent(_aimAzimuth, _aimElevation));
                    Engine!.SendEvent(GunModel, new FireEvent("on"));
                }

                // C2에 사격 시작 보고
                SendEngagementResult("fire_start");
            }
        }
        else if (Phase == PhaseType.FireOn)
        {
            // 교전 중 조준 갱신
            if (GunModel is not null)
            {
                Engine!.SendEvent(GunModel, new DriveEvent(_aimAzimuth, _aimElevation));
            }
        }

        return TContinue;
    }

    // ── ⓘ DriveResult (Gun → FCS, 200Hz) ──

    private double HandleDriveResult(double t, DriveResultEvent ev)
    {
        _bulletFire = ev.BulletFire;
        _bulletRemain = ev.BulletRemain;
        _firedCount = ev.BulletFire;

        if (ev.BulletRemain <= 0 && Phase == PhaseType.FireOn)
        {
            Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] Gun ammo depleted\n");
            EndEngagement(t, "fail");
        }

        return TContinue;
    }

    // ── ⓘ BulletPosition (Bullet → FCS) ──

    private double HandleBulletPosition(double t, BulletPositionEvent ev)
    {
        // 탄 위치 추적 (향후 DamageEval에 활용)
        return TContinue;
    }

    // ── ⓘ Destroyed (Target → FCS) ──

    private double HandleDestroyed(double t, DestroyedEvent ev)
    {
        if (_target is null || _target.Id != ev.TargetId)
            return TContinue;

        Phase = PhaseType.TgtDestroyed;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Target {ev.TargetId} destroyed\n");

        DamageEval(t);
        EndEngagement(t, "success");
        return TInfinite;
    }

    // ── ⓘ FailEvent (AssetZone → C2 → FCS) ──

    private double HandleFail(double t, FailEvent ev)
    {
        if (_target is null || _target.Id != ev.Target.Id)
            return TContinue;

        Phase = PhaseType.FireOff;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Intercept FAILED for [{_target.Name}]\n");
        EndEngagement(t, "fail");
        return TInfinite;
    }

    // ── ⓘ TrackLost (TrackRadar → FCS) ──

    private double HandleTrackLost(double t, TrackLostEvent ev)
    {
        if (_target is null || _target.Id != ev.TargetId)
            return TContinue;

        Phase = PhaseType.FireOff;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Track lost for target {ev.TargetId}\n");

        EndEngagement(t, "fail");
        return TInfinite;
    }

    // ── ⓘ CollideEvent (표적 → FCS 피격) ──

    private double HandleCollide(double t, CollideEvent ev)
    {
        Health -= ev.Power;
        if (Health > 0.0)
        {
            Phase = PhaseType.AttackedAlive;
            Logger.Dbg(DbgFlag.Collide,
                $"{t:F6} [{Name}] Attacked, alive (health={Health:F1})\n");
        }
        else
        {
            Phase = PhaseType.AttackedDie;
            Logger.Dbg(DbgFlag.Collide,
                $"{t:F6} [{Name}] Attacked, destroyed\n");
            Phase = PhaseType.Disabled;
            IsEnabled = false;
        }
        return TContinue;
    }

    // ── 함수 ──

    /// <summary>PHP 계산 (PhpErr 반영).</summary>
    public double PHPCalc()
    {
        if (_firedCount <= 0) return 0.0;
        double rawPhp = (double)_hitCount / _firedCount;
        return Math.Max(0.0, rawPhp + PhpErr);
    }

    /// <summary>피해 평가.</summary>
    public void DamageEval(double t)
    {
        double php = PHPCalc();
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] DamageEval: fired={_firedCount}, hit={_hitCount}, PHP={php:F4}\n");
    }

    // ── 교전 종료 + 보고 ──

    private void EndEngagement(double t, string status)
    {
        // Gun 사격 중지
        if (GunModel is not null && Phase != PhaseType.Wait)
        {
            Engine!.SendEvent(GunModel, new FireEvent("off"));
        }

        // TrackRadar 추적 중지
        if (TrackRadar is not null && _target is not null)
        {
            Engine!.SendEvent(TrackRadar,
                new TrackOrderEvent(_target.Id, "stop", 0));
        }

        // C2에 최종 교전 결과 보고
        SendEngagementResult(status);

        // Health 보고
        if (C2 is not null)
        {
            Engine!.SendEvent(C2, new HealthEvent(Id, Health));
        }

        ReturnToWait();
    }

    private void SendEngagementResult(string status)
    {
        if (C2 is null || _target is null) return;

        Engine!.SendEvent(C2, new EngagementResultEvent(
            _target.Id, _aimAzimuth, _aimElevation,
            _bulletFire, _bulletRemain, status));
    }

    private void ReturnToWait()
    {
        Phase = PhaseType.Wait;
        _target = null;
        ResetStats();
    }

    private void ResetStats()
    {
        _firedCount = 0;
        _hitCount = 0;
        _bulletFire = 0;
        _bulletRemain = 0;
    }
}

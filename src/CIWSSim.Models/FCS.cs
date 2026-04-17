using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 사격통제 시스템 (FCS) CIWS당 1개.
/// TrackRadar에 추적 명령, Gun에 구동/사격 명령을 내리고
/// 피해평가(PHP)를 수행한다.
/// </summary>
public class FCS : Model
{
    /// <summary>
    /// 사격 가능 거리 (m)
    /// </summary>
    public double FireRange { get; set; } = 1500.0;

    /// <summary>
    /// 소속 CIWS ID
    /// </summary>
    public int CiwsId { get; set; }

    /// <summary>
    /// PHP 오차 모델
    /// </summary>
    public double PhpErr { get; set; }

    /// <summary>
    /// 추적 주기 (TrackRadar에 전달)
    /// </summary>
    public double TrackPeriod { get; set; } = 0.04;

    /// <summary>
    /// 교전 결과 보고 주기 (초)
    /// </summary>
    public double EngagementReportPeriod { get; set; } = 1.0;

    /// <summary>
    /// 사격이 이 시간(초) 이상 연속 유지되면 요격 성공으로 판정
    /// </summary>
    // Test 지속 사격 요격 판정 프로퍼티 (탄환-표적 충돌 방식으로 돌릴 때 제거)
    public double SustainedFireKillSec { get; set; } = 5.0;

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

    // Test ── 지속 사격 판정 ── (탄환-표적 충돌 방식 복원 시 제거)
    private double _firingStartT = -1.0;  // FireStatus.Firing이 시작된 시각. 끊기면 -1로 초기화
    private bool _killSent;                // AttackEvent 중복 송신 방지

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
            SendEngagementResult(EngagementStatus.Engaging);
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

            case BulletImpactEvent impact:
                return HandleBulletImpact(t, impact);

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

    // ── input: TargetDesignation (C2 → FCS) ──

    private double HandleTargetDesignation(double t, TargetDesignationEvent ev)
    {
        if (ev.Cmd == DesignationCmd.Start)
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
                    new TrackOrderEvent(ev.TargetId, TrackOrderCmd.Start, TrackPeriod, _target));
            }

            return EngagementReportPeriod;
        }
        else if (ev.Cmd == DesignationCmd.Stop)
        {
            Logger.Dbg(DbgFlag.Init,
                $"{t:F6} [{Name}] Designation stop for target {ev.TargetId}\n");
            EndEngagement(t, EngagementStatus.Fail);
            return TInfinite;
        }

        return TContinue;
    }

    // ── input: TrackInfo (TrackRadar → FCS) ──

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

        // 추적 중에는 사거리와 무관하게 항상 Gun에 조준 명령 (실 CIWS 동작에 가깝게)
        if (GunModel is not null)
        {
            Engine!.SendEvent(GunModel, new DriveEvent(_aimAzimuth, _aimElevation));
        }

        if (Phase == PhaseType.StartEngage || Phase == PhaseType.TrackRcvd)
        {
            Phase = PhaseType.TrackRcvd;

            // 사거리 진입 시점에만 사격 시작
            if (_trackDist <= FireRange)
            {
                Phase = PhaseType.FireOn;
                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] FireOn [{_target.Name}] dist={_trackDist:F1}m\n");

                if (GunModel is not null)
                {
                    // 260415 Gun이 CIWS.csv에 사격 대상 InputId와 Tag를 기록할 수 있도록 전달
                    // Engine!.SendEvent(GunModel, new FireEvent(FireCmd.On));
                    Engine!.SendEvent(GunModel, new FireEvent(FireCmd.On, _target.InputId, _target.Tag));
                }

                // C2에 사격 시작 보고
                SendEngagementResult(EngagementStatus.FireStart);
            }
        }

        return TContinue;
    }

    // ── input: DriveResult (Gun → FCS, 200Hz) ──

    private double HandleDriveResult(double t, DriveResultEvent ev)
    {
        _bulletFire = ev.BulletFire;
        _bulletRemain = ev.BulletRemain;
        _firedCount = ev.BulletFire;

        // Test 연속 사격 시간 추적. FireStatus.Firing이 SustainedFireKillSec 이상 유지되면
        // 표적에 Health 전량 피해 AttackEvent → TargetBase가 Destroyed → HandleDestroyed 경로 진입
        // 탄환-표적 충돌 방식 복원 시 이 블록 전체 제거
        if (ev.FireStatus == FireStatus.Firing && Phase == PhaseType.FireOn)
        {
            if (_firingStartT < 0.0)
            {
                _firingStartT = t;
            }
            else if (!_killSent && (t - _firingStartT) >= SustainedFireKillSec
                     && _target is not null && _target.IsEnabled)
            {
                _killSent = true;
                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] Sustained fire {SustainedFireKillSec:F1}s → kill [{_target.Name}]\n");
                Engine!.SendEvent(_target, new AttackEvent(_target.Health, this));
            }
        }
        else
        {
            // 사격 끊기면 카운터 리셋 (연속 유지만 인정)
            _firingStartT = -1.0;
        }

        if (ev.BulletRemain <= 0 && Phase == PhaseType.FireOn)
        {
            Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] Gun ammo depleted\n");
            EndEngagement(t, EngagementStatus.Fail);
        }

        return TContinue;
    }

    // ── input: BulletImpact (Gun → FCS) ──

    private double HandleBulletImpact(double t, BulletImpactEvent ev)
    {
        if (ev.IsHit) _hitCount++;
        return TContinue;
    }

    // ── input: Destroyed (Target → FCS) ──

    private double HandleDestroyed(double t, DestroyedEvent ev)
    {
        if (_target is null || _target.Id != ev.TargetId)
            return TContinue;

        Phase = PhaseType.TgtDestroyed;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Target {ev.TargetId} destroyed\n");

        DamageEval(t);
        EndEngagement(t, EngagementStatus.Success);
        return TInfinite;
    }

    // ── input: FailEvent (AssetZone → C2 → FCS) ──

    private double HandleFail(double t, FailEvent ev)
    {
        if (_target is null || _target.Id != ev.Target.Id)
            return TContinue;

        Phase = PhaseType.FireOff;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Intercept FAILED for [{_target.Name}]\n");
        EndEngagement(t, EngagementStatus.Fail);
        return TInfinite;
    }

    // ── input: TrackLost (TrackRadar → FCS) ──

    private double HandleTrackLost(double t, TrackLostEvent ev)
    {
        if (_target is null || _target.Id != ev.TargetId)
            return TContinue;

        Phase = PhaseType.FireOff;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Track lost for target {ev.TargetId}\n");

        EndEngagement(t, EngagementStatus.Fail);
        return TInfinite;
    }

    // ── input: CollideEvent (표적 → FCS 피격) ──

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

    /// <summary>
    /// PHP 계산 (PhpErr 반영)
    /// </summary>
    public double PHPCalc()
    {
        if (_firedCount <= 0) return 0.0;
        double rawPhp = (double)_hitCount / _firedCount;
        return Math.Max(0.0, rawPhp + PhpErr);
    }

    /// <summary>
    /// 피해 평가
    /// </summary>
    public void DamageEval(double t)
    {
        double php = PHPCalc();
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] DamageEval: fired={_firedCount}, hit={_hitCount}, PHP={php:F4}\n");
    }

    // ── 교전 종료 + 보고 ──

    private void EndEngagement(double t, EngagementStatus status)
    {
        // Gun 사격 중지
        if (GunModel is not null && Phase != PhaseType.Wait)
        {
            Engine!.SendEvent(GunModel, new FireEvent(FireCmd.Off));
        }

        // TrackRadar 추적 중지
        if (TrackRadar is not null && _target is not null)
        {
            Engine!.SendEvent(TrackRadar,
                new TrackOrderEvent(_target.Id, TrackOrderCmd.Stop, 0));
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

    private void SendEngagementResult(EngagementStatus status)
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
        // Test 지속 사격 판정 상태 초기화 (복원 시 이 두 줄 제거)
        _firingStartT = -1.0;
        _killSent = false;
    }
}

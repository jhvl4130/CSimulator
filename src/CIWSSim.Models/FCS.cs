using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

/// <summary>
/// 사격통제 시스템 (FCS). CIWS당 1개.
/// TrackRadar에 추적 명령, EOTS에 종속 추적 명령, Gun에 사격 명령을 내리고
/// 피해평가(PHP)를 수행한다.
/// </summary>
public class FCS : Model
{
    /// <summary>사격 가능 거리 (m).</summary>
    public double FireRange { get; set; } = 1500.0;

    /// <summary>소속 CIWS ID.</summary>
    public int CiwsId { get; set; }

    // ── 참조 (생성 시 주입) ──
    public Model? TrackRadar { get; set; }
    public Model? EotsModel { get; set; }
    public Model? GunModel { get; set; }
    public Model? C2 { get; set; }

    // ── 교전 대상 ──
    private Model? _target;

    // ── 최신 추적 데이터 ──
    private XYZPos _trackPos;
    private XYZPos _trackVel;

    // ── 교전 통계 (PHP 계산용) ──
    private int _firedCount;
    private int _hitCount;
    private double _totalDamage;

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
        return TInfinite;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        switch (ev)
        {
            case AssignEvent assign:
                return HandleAssign(t, assign);

            case TrackDataEvent trackData:
                return HandleTrackData(t, trackData);

            case EotsDataEvent eotsData:
                return HandleEotsData(t, eotsData);

            case HitResultEvent hitResult:
                return HandleHitResult(t, hitResult);

            case FailEvent fail:
                return HandleFail(t, fail);

            case TrackLostEvent trackLost:
                return HandleTrackLost(t, trackLost);

            case StatusEvent status:
                return HandleStatus(t, status);

            case CollideEvent collide:
                return HandleCollide(t, collide);
        }

        return TContinue;
    }

    private double HandleAssign(double t, AssignEvent ev)
    {
        if (Phase != PhaseType.Wait)
        {
            Logger.Warn($"{t:F6} [{Name}] Assign rejected: busy (phase={Phase})\n");
            return TContinue;
        }

        _target = ev.Target;
        Phase = PhaseType.StartEngage;
        ResetStats();

        Logger.Dbg(DbgFlag.Init,
            $"{t:F6} [{Name}] Assigned [{_target.Name}]\n");

        // TrackRadar에 추적 명령
        if (TrackRadar is not null)
        {
            Engine!.SendEvent(TrackRadar, new TrackCmdEvent(_target));
        }

        return TContinue;
    }

    private double HandleTrackData(double t, TrackDataEvent ev)
    {
        if (Phase != PhaseType.StartEngage && Phase != PhaseType.TrackRcvd
            && Phase != PhaseType.FireOn)
            return TContinue;

        _trackPos = ev.Pos;
        _trackVel = ev.Vel;
        Phase = PhaseType.TrackRcvd;

        // EOTS에 종속 추적 명령
        if (EotsModel is not null)
        {
            Engine!.SendEvent(EotsModel, new EotsCmdEvent(ev.Pos));
        }

        return TContinue;
    }

    private double HandleEotsData(double t, EotsDataEvent ev)
    {
        if (_target is null || !_target.IsEnabled)
            return TContinue;

        if (Phase == PhaseType.TrackRcvd)
        {
            // 사격 가능 거리 판단
            double dist = GeoUtil.Distance(Pos, _trackPos);
            if (dist <= FireRange)
            {
                Phase = PhaseType.FireOn;
                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] FireOn [{_target.Name}] dist={dist:F1}m\n");

                // Gun에 사격 명령
                if (GunModel is not null)
                {
                    Engine!.SendEvent(GunModel,
                        new FireCmdEvent(ev.Azimuth, ev.Elevation, _target));
                }
            }
        }
        else if (Phase == PhaseType.FireOn)
        {
            // 교전 중 조준 갱신: Gun에 새 사격 명령
            if (GunModel is not null)
            {
                Engine!.SendEvent(GunModel,
                    new FireCmdEvent(ev.Azimuth, ev.Elevation, _target));
            }
        }

        return TContinue;
    }

    private double HandleHitResult(double t, HitResultEvent ev)
    {
        _firedCount++;
        if (ev.IsHit)
        {
            _hitCount++;
            _totalDamage += ev.Damage;
        }

        // 표적이 격파되었는지 확인
        if (_target is not null && !_target.IsEnabled)
        {
            Phase = PhaseType.TgtDestroyed;
            Logger.Dbg(DbgFlag.Collide,
                $"{t:F6} [{Name}] Target [{_target.Name}] destroyed\n");
            DoAssessment(t, true);
        }

        return TContinue;
    }

    private double HandleFail(double t, FailEvent ev)
    {
        if (_target is null || _target.Id != ev.Target.Id)
            return TContinue;

        Phase = PhaseType.FireOff;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Intercept FAILED for [{_target.Name}]\n");
        DoAssessment(t, false);
        return TContinue;
    }

    private double HandleTrackLost(double t, TrackLostEvent ev)
    {
        if (_target is null || _target.Id != ev.TargetId)
            return TContinue;

        Phase = PhaseType.FireOff;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Track lost for target {ev.TargetId}\n");

        // C2에 보고
        if (C2 is not null)
        {
            Engine!.SendEvent(C2, new StatusEvent("TrackLost",
                $"CIWS {CiwsId}: track lost for target {ev.TargetId}"));
        }

        ReturnToWait();
        return TContinue;
    }

    private double HandleStatus(double t, StatusEvent ev)
    {
        // Gun으로부터 탄약 소진 등 상태 보고
        if (ev.EventType == "AmmoOut")
        {
            Phase = PhaseType.FireOff;
            Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] Gun ammo depleted\n");
            if (_target is not null)
            {
                bool killed = !_target.IsEnabled;
                DoAssessment(t, killed);
            }
        }
        return TContinue;
    }

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

    private void DoAssessment(double t, bool killed)
    {
        // PHP 계산
        double php = _firedCount > 0 ? (double)_hitCount / _firedCount : 0.0;
        string result = killed ? "KILL" : "MISS";

        string desc = $"CIWS {CiwsId}: target={_target?.Id}, result={result}, " +
                       $"fired={_firedCount}, hit={_hitCount}, PHP={php:F4}, damage={_totalDamage:F1}";

        Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] Assessment: {desc}\n");

        // C2에 보고
        if (C2 is not null)
        {
            Engine!.SendEvent(C2, new StatusEvent("Assessment", desc));
        }

        ReturnToWait();
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
        _totalDamage = 0.0;
    }
}

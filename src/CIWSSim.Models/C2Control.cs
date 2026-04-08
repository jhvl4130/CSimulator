using System.Text;
using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 통제소 (C2Control). 전체 시스템에 1개.
/// 위협순위 계산, CIWS별 표적 할당, 이벤트 로그 출력을 담당한다.
/// </summary>
public class C2Control : Model
{
    /// <summary>FCS 리스트 (생성 시 주입).</summary>
    public List<Model> FcsList { get; } = new();

    /// <summary>표적 ID → 할당된 FCS 매핑.</summary>
    private readonly Dictionary<int, Model> _targetFcsMap = new();

    /// <summary>최신 추적 데이터 (표적 ID → TrackInfo).</summary>
    private readonly Dictionary<int, TrackInfoEvent> _latestTrackData = new();

    /// <summary>이벤트 로그 StreamWriter.</summary>
    private StreamWriter? _logWriter;

    /// <summary>이벤트 로그 출력 경로.</summary>
    public string? EventLogPath { get; set; } = "event_log.csv";

    public C2Control(int id) : base(id)
    {
        Class = ModelClass.C2;
        Type = MtControl;
        Name = $"C2Control-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.Run;
        IsEnabled = true;

        // 이벤트 로그 초기화
        if (EventLogPath is not null)
        {
            _logWriter = new StreamWriter(EventLogPath, false, Encoding.UTF8);
            _logWriter.WriteLine("Time,EventType,SourceID,TargetID,CiwsID,Detail");
        }

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
            case TrackInfoEvent trackInfo:
                return HandleTrackInfo(t, trackInfo);

            case EngagementResultEvent engResult:
                return HandleEngagementResult(t, engResult);

            case HealthEvent health:
                return HandleHealth(t, health);

            case FailEvent fail:
                return HandleFail(t, fail);
        }

        return TContinue;
    }

    // ── ⓘ TrackInfo (SearchRadar → C2, 주기적) ──

    private double HandleTrackInfo(double t, TrackInfoEvent ev)
    {
        // 최신 추적 데이터 갱신
        _latestTrackData[ev.TargetId] = ev;

        // 이미 할당된 표적이면 추적 데이터만 갱신
        if (_targetFcsMap.ContainsKey(ev.TargetId))
            return TContinue;

        // 위협평가 + 할당
        var target = Engine!.GetModel(ev.TargetId);
        if (target is null || !target.IsEnabled)
            return TContinue;

        var fcs = TargetAlloc(t, ev.TargetId, target);
        if (fcs is null)
        {
            Logger.Warn($"{t:F6} [{Name}] No available FCS for target {ev.TargetId}\n");
            return TContinue;
        }

        // 할당
        _targetFcsMap[ev.TargetId] = fcs;
        int ciwsId = (fcs is FCS f) ? f.CiwsId : fcs.Id;

        Logger.Dbg(DbgFlag.Init,
            $"{t:F6} [{Name}] Assign target {ev.TargetId} → CIWS {ciwsId}\n");
        WriteLog(t, "Assign", Id, ev.TargetId, ciwsId,
            $"Target {ev.TargetId} assigned to CIWS {ciwsId}");

        Engine!.SendEvent(fcs, new TargetDesignationEvent("start", ev.TargetId, target));

        return TContinue;
    }

    // ── ⓘ EngagementResult (FCS → C2, 주기적 + 종료) ──

    private double HandleEngagementResult(double t, EngagementResultEvent ev)
    {
        int ciwsId = 0;
        if (_targetFcsMap.TryGetValue(ev.TargetId, out var fcs))
            ciwsId = (fcs is FCS f) ? f.CiwsId : fcs.Id;

        // 사격 시작/종료(성공/실패)만 로깅
        if (ev.Status == "fire_start")
        {
            WriteLog(t, "FireStart", Id, ev.TargetId, ciwsId,
                $"az={ev.Azimuth:F1} el={ev.Elevation:F1}");
        }
        else if (ev.Status == "success")
        {
            WriteLog(t, "Kill", Id, ev.TargetId, ciwsId,
                $"fired={ev.BulletFire} remain={ev.BulletRemain}");
            _targetFcsMap.Remove(ev.TargetId);
            _latestTrackData.Remove(ev.TargetId);
        }
        else if (ev.Status == "fail")
        {
            WriteLog(t, "Miss", Id, ev.TargetId, ciwsId,
                $"fired={ev.BulletFire} remain={ev.BulletRemain}");
            _targetFcsMap.Remove(ev.TargetId);
            _latestTrackData.Remove(ev.TargetId);
        }

        return TContinue;
    }

    // ── ⓘ Health ──

    private double HandleHealth(double t, HealthEvent ev)
    {
        return TContinue;
    }

    // ── ⓘ FailEvent (AssetZone → C2) ──

    private double HandleFail(double t, FailEvent ev)
    {
        var target = ev.Target;

        WriteLog(t, "Fail", Id, target.Id, 0,
            $"Target {target.Id} reached defense zone - intercept failed");

        // 해당 표적을 담당하는 FCS에 FailEvent 전달
        if (_targetFcsMap.TryGetValue(target.Id, out var fcs))
        {
            Engine!.SendEvent(fcs, ev);
            _targetFcsMap.Remove(target.Id);
        }

        return TContinue;
    }

    // ── ⓕ 위협평가 ──

    /// <summary>위협 우선순위 평가 (거리 기반: 가까운 것 우선).</summary>
    public List<int> ThreatEval()
    {
        var threats = new List<(int id, double dist)>();

        foreach (var (id, info) in _latestTrackData)
        {
            if (_targetFcsMap.ContainsKey(id)) continue;
            double dist = GeoUtil.Distance(Pos, info.Pos);
            threats.Add((id, dist));
        }

        threats.Sort((a, b) => a.dist.CompareTo(b.dist));
        return threats.Select(t => t.id).ToList();
    }

    // ── ⓕ 표적 할당 ──

    /// <summary>ThreatEval 기반 표적 할당. 가용 FCS 반환.</summary>
    public Model? TargetAlloc(double t, int targetId, Model target)
    {
        foreach (var fcs in FcsList)
        {
            if (fcs is FCS fcsModel && fcsModel.Phase == PhaseType.Wait)
            {
                return fcs;
            }
        }
        return null;
    }

    // ── 로그 ──

    private void WriteLog(double t, string eventType, int sourceId, int targetId,
        int ciwsId, string detail)
    {
        _logWriter?.WriteLine(
            $"{t:F4},{eventType},{sourceId},{targetId},{ciwsId},\"{detail}\"");
        _logWriter?.Flush();
    }

    /// <summary>시뮬레이션 종료 시 StreamWriter 정리.</summary>
    public void Dispose()
    {
        _logWriter?.Dispose();
        _logWriter = null;
    }
}

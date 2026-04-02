using System.Text;
using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
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
            case DetectEvent detect:
                return HandleDetect(t, detect);

            case StatusEvent status:
                HandleStatusReport(t, status);
                return TContinue;

            case FailEvent fail:
                return HandleFail(t, fail);
        }

        return TContinue;
    }

    private double HandleDetect(double t, DetectEvent ev)
    {
        var target = ev.Target;

        // 이미 할당된 표적이면 무시
        if (_targetFcsMap.ContainsKey(target.Id))
            return TContinue;

        // 위협순위 계산 (추후 구현) — 현재는 첫 번째 가용 FCS에 할당
        Model? assignedFcs = null;
        foreach (var fcs in FcsList)
        {
            if (fcs is FCS fcsModel && fcsModel.Phase == PhaseType.Wait)
            {
                assignedFcs = fcs;
                break;
            }
        }

        if (assignedFcs is null)
        {
            Logger.Warn($"{t:F6} [{Name}] No available FCS for [{target.Name}]\n");
            WriteLog(t, "DetectNoFCS", Id, target.Id, 0,
                $"No available FCS for target {target.Id}");
            return TContinue;
        }

        // 할당
        _targetFcsMap[target.Id] = assignedFcs;
        int ciwsId = (assignedFcs is FCS f) ? f.CiwsId : assignedFcs.Id;

        Logger.Dbg(DbgFlag.Init,
            $"{t:F6} [{Name}] Assign [{target.Name}] → CIWS {ciwsId}\n");
        WriteLog(t, "Assign", Id, target.Id, ciwsId,
            $"Target {target.Id} assigned to CIWS {ciwsId}");

        Engine!.SendEvent(assignedFcs, new AssignEvent(target));

        return TContinue;
    }

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

    private void HandleStatusReport(double t, StatusEvent ev)
    {
        WriteLog(t, ev.EventType, Id, 0, 0, ev.Description);

        // Assessment 완료 시 표적-FCS 매핑 정리
        if (ev.EventType == "Assessment" || ev.EventType == "TrackLost")
        {
            // 완료된 FCS의 표적 매핑 제거
            var toRemove = new List<int>();
            foreach (var (targetId, fcs) in _targetFcsMap)
            {
                if (fcs is FCS fcsModel && fcsModel.Phase == PhaseType.Wait)
                {
                    toRemove.Add(targetId);
                }
            }
            foreach (var id in toRemove)
                _targetFcsMap.Remove(id);
        }
    }

    private void WriteLog(double t, string eventType, int sourceId, int targetId,
        int ciwsId, string detail)
    {
        _logWriter?.WriteLine(
            $"{t:F4},{eventType},{sourceId},{targetId},{ciwsId},\"{detail}\"");
        _logWriter?.Flush();
    }

    /// <summary>시뮬레이션 종료 시 StreamWriter 정리. Engine.Start() 이후 호출.</summary>
    public void Dispose()
    {
        _logWriter?.Dispose();
        _logWriter = null;
    }
}

using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

/// <summary>
/// 탐지 레이더. 탐지 범위 내 Target을 주기적으로 탐지하여
/// C2Control에 DetectEvent를 전송한다.
/// </summary>
public class SearchRadar : Model
{
    /// <summary>탐지 반경 (m).</summary>
    public double DetectRange { get; set; }

    /// <summary>탐지 주기 (초).</summary>
    public double DetectPeriod { get; set; } = 1.0;

    /// <summary>C2Control 참조 (생성 시 주입).</summary>
    public Model? C2 { get; set; }

    /// <summary>이미 탐지 보고한 표적 ID 집합 (중복 보고 방지).</summary>
    private readonly HashSet<int> _detectedIds = new();

    public SearchRadar(int id) : base(id)
    {
        Class = ModelClass.Sensor;
        Type = MtSRadar;
        Name = $"SearchRadar-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.Run;
        IsEnabled = true;
        return DetectPeriod;
    }

    public override double IntTrans(double t)
    {
        if (Phase != PhaseType.Run || !IsEnabled)
            return TInfinite;

        foreach (var target in Engine!.GetModelsByClass(ModelClass.Target))
        {
            if (!target.IsEnabled) continue;
            if (_detectedIds.Contains(target.Id)) continue;

            double dist = GeoUtil.Distance(Pos, target.Pos);
            if (dist <= DetectRange)
            {
                _detectedIds.Add(target.Id);
                Logger.Dbg(DbgFlag.Init,
                    $"{t:F6} [{Name}] Detected [{target.Name}] dist={dist:F1}m\n");

                if (C2 is not null)
                {
                    Engine.SendEvent(C2, new DetectEvent(target, target.Pos, target.Speed));
                }
            }
        }

        return DetectPeriod;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }
}

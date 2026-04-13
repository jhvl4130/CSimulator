using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 반구 영역 방어 모델. 표적이 반구에 진입하면 요격 실패로 판정하고
/// C2Control에 FailEvent를 전달한다.
/// </summary>
public class AssetZone : Model
{
    /// <summary>반구 반경 (m)</summary>
    public double Radius { get; set; }

    /// <summary>방어존에 진입한 표적 수</summary>
    public int HitCount { get; internal set; }

    /// <summary>C2Control 참조 (생성 시 주입)</summary>
    public Model? C2 { get; set; }

    public AssetZone(int id) : base(id)
    {
        Class = ModelClass.Asset;
        Type = MtAssetZone;
        Name = $"AssetZone-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.Run;
        IsEnabled = true;
        return TInfinite;
    }

    public override double IntTrans(double t)
    {
        return TInfinite;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }
}

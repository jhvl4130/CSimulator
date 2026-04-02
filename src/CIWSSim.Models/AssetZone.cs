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
    /// <summary>반구 반경 (m).</summary>
    public double Radius { get; set; }

    /// <summary>C2Control 참조 (생성 시 주입).</summary>
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
        return MovePeriod;
    }

    public override double IntTrans(double t)
    {
        if (Phase != PhaseType.Run || !IsEnabled)
            return TInfinite;

        foreach (var target in Engine!.GetModelsByClass(ModelClass.Target))
        {
            if (!target.IsEnabled) continue;
            if (CollisionDetection.IsInsideHemisphere(target.Pos, Pos, Radius))
            {
                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] ↔ [{target.Name}] Intercept FAILED (reached zone)\n");

                // C2Control에 요격 실패 보고
                if (C2 is not null)
                {
                    Engine.SendEvent(C2, new FailEvent(target));
                }

                // 표적 파괴 (방어존 도달 = 피해 발생)
                Engine.SendEvent(target, new CollideEvent(target.Power));
            }
        }

        return MovePeriod;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }
}

using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

/// <summary>
/// 반구 영역 방어 모델. 중심점과 반경으로 정의된 반구 내에
/// Target이 진입하면 충돌 판정을 수행한다.
/// </summary>
public class AssetZone : Model
{
    /// <summary>반구 반경 (m).</summary>
    public double Radius { get; set; }

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
                    $"{t:F6} [{Name}] ↔ [{target.Name}] Collide (hemisphere)\n");

                // Target에 충돌 이벤트 전송 → Target 파괴
                Engine.SendEvent(target, new CollideEvent(target.Power));

                // AssetZone도 피해를 받음
                Health -= target.Power;
                if (Health < 0.0)
                    Health = 0.0;

                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] health={Health:F6} power={target.Power:F6}\n");

                if (Health <= 0.0)
                {
                    Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] killed\n");
                    IsEnabled = false;
                    return TInfinite;
                }
            }
        }

        return MovePeriod;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        if (ev is CollideEvent col)
        {
            Health -= col.Power;
            if (Health < 0.0)
                Health = 0.0;

            Logger.Dbg(DbgFlag.Collide,
                $"{t:F6} [{Name}] Collide : health={Health:F6} power={col.Power:F6}\n");

            if (Health <= 0.0)
            {
                Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] killed\n");
                IsEnabled = false;
            }
        }

        return TContinue;
    }
}

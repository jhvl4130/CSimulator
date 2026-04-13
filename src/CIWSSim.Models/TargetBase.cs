using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;

namespace CIWSSimulator.Models;

/// <summary>
/// 비행체 공통 base (Airplane, Rocket, Drone 등)
/// 건물/AssetZone 충돌 판정, 피격 처리, 종료 로직을 공유한다.
/// </summary>
public abstract class TargetBase : Model
{
    /// <summary>표적 상태 (생존/파괴/충돌) Target.csv 출력용</summary>
    public TargetStatus Status { get; set; } = TargetStatus.Alive;

    protected TargetBase(int id) : base(id)
    {
        Class = ModelClass.Target;
    }

    /// <summary>건물 충돌 판정. 충돌 시 true 반환 (IsEnabled=false 됨)</summary>
    protected bool CheckBuildingCollision(double t)
    {
        foreach (var asset in Engine!.GetModelsByClass(ModelClass.Asset))
        {
            if (!asset.IsEnabled) continue;
            if (asset is AssetZone) continue;
            if (CollisionDetection.IsCollide(Pos, asset.Building))
            {
                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] ↔ [{asset.Name}] Collide\n");
                Engine.SendEvent(asset, new CollideEvent(Power));
                EndTarget();
                return true;
            }
        }
        return false;
    }

    /// <summary>AssetZone 반구 진입 판정. 진입 시 true 반환 (IsEnabled=false 됨)</summary>
    protected bool CheckAssetZoneCollision(double t)
    {
        foreach (var asset in Engine!.GetModelsByClass(ModelClass.Asset))
        {
            if (!asset.IsEnabled) continue;
            if (asset is not AssetZone zone) continue;
            if (CollisionDetection.IsInsideHemisphere(Pos, zone.Pos, zone.Radius))
            {
                Logger.Dbg(DbgFlag.Collide,
                    $"{t:F6} [{Name}] ↔ [{zone.Name}] Intercept FAILED (reached zone)\n");

                Status = TargetStatus.Collided;
                IsStateChanged = true;

                if (zone.C2 is not null)
                {
                    Engine.SendEvent(zone.C2, new FailEvent(this));
                }

                zone.HitCount++;
                Engine.SendEvent(this, new CollideEvent(Power));
                return true;
            }
        }
        return false;
    }

    /// <summary>피격 처리 (AttackEvent)</summary>
    protected void HandleAttack(double t, AttackEvent attack)
    {
        Health -= attack.Power;
        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Attacked, health={Health:F1}\n");
        if (Health <= 0.0)
        {
            Status = TargetStatus.Destroyed;
            IsStateChanged = true;

            if (attack.SourceFcs is not null)
            {
                Engine!.SendEvent(attack.SourceFcs, new DestroyedEvent(Id));
            }
            EndTarget();
        }
    }

    /// <summary>방어존/외부 충돌에 의한 파괴 처리</summary>
    protected void HandleCollide(double t)
    {
        Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] destroyed by defense zone\n");
        EndTarget();
    }

    protected void EndTarget()
    {
        Phase = PhaseType.End;
        IsEnabled = false;
        Engine?.RemoveModel(Id);
    }
}

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
    /// <summary>
    /// 표적 상태 (생존/파괴/충돌) Target.csv 출력용
    /// </summary>
    public TargetStatus Status { get; set; } = TargetStatus.Alive;

    // 260415 최종 목표 지점 (ENU). 여기 지나치면 비행 종료.
    public XYZPos Destination { get; set; }

    // 260415 AssetZone 진입 1회 플래그 (이벤트 중복 송신 방지)
    private bool _zoneBreached;

    protected TargetBase(int id) : base(id)
    {
        Class = ModelClass.Target;
    }

    /// <summary>
    /// 건물 충돌 판정. 충돌 시 true 반환 (IsEnabled=false 됨)
    /// </summary>
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

    /// <summary>
    /// AssetZone 반구 진입 판정. 최초 진입 시 Status=Collided로 전환하고 C2에 Fail 통지.
    /// 표적은 계속 비행한다 (목표 지점 도달 시 비로소 EndTarget).
    /// </summary>
    // 260415 진입 즉시 삭제 → 요격/할당 불가 상태로만 전환하도록 변경. CollideEvent 송신·EndTarget 제거.
    protected void CheckAssetZoneCollision(double t)
    {
        if (_zoneBreached) return;

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
                _zoneBreached = true;

                if (zone.C2 is not null)
                {
                    Engine.SendEvent(zone.C2, new FailEvent(this));
                }
                zone.HitCount++;
                return;
            }
        }
    }

    /// <summary>
    /// 목표 지점(Destination) 통과 판정. 지나쳤으면 EndTarget 후 true.
    /// </summary>
    // 260415 표적이 목표지점 지나치면 자연 소멸
    protected bool CheckDestinationReached(double t)
    {
        double yawRad = GeoUtil.DegToRad(Pose.Yaw);
        double pitchRad = GeoUtil.DegToRad(Pose.Pitch);
        double fx = Math.Sin(yawRad) * Math.Cos(pitchRad);
        double fy = Math.Cos(yawRad) * Math.Cos(pitchRad);
        double fz = Math.Sin(pitchRad);

        double dx = Destination.X - Pos.X;
        double dy = Destination.Y - Pos.Y;
        double dz = Destination.Z - Pos.Z;

        double dot = fx * dx + fy * dy + fz * dz;
        if (dot <= 0.0)
        {
            Logger.Dbg(DbgFlag.Move, $"{t:F6} [{Name}] destination reached\n");
            EndTarget();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 피격 처리 (AttackEvent)
    /// </summary>
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

    /// <summary>
    /// 방어존/외부 충돌에 의한 파괴 처리
    /// </summary>
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

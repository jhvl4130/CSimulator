using System;

namespace CIWSSimulator.Core.Geometry;

/// <summary>
/// 웨이포인트 기반 Fly-Over 이동 로직.
/// 비행체는 웨이포인트에 스냅하지 않고, 선회율 제한 내에서 자연스럽게
/// 회전하며 통과한다. 웨이포인트 통과 판정은 진행 방향 기준 내적으로 수행.
/// </summary>
public class WaypointMover
{
    private int _waypointIndex;

    public double TurnRate { get; set; } = 30.0;   // 선회율 (deg/sec)
    public double AltRate { get; set; } = 50.0;     // 고도 변화율 (m/sec)

    /// <summary>현재 추종 중인 웨이포인트 인덱스.</summary>
    public int WaypointIndex => _waypointIndex;

    /// <summary>모든 웨이포인트를 소진했는지 여부.</summary>
    public bool IsFinished(Model model) => _waypointIndex >= model.Waypoints.Count;

    public void Reset()
    {
        _waypointIndex = 0;
    }

    /// <summary>
    /// 모델의 위치/자세/속도를 한 스텝(dt) 전진시킨다.
    /// 웨이포인트 통과 시 true를 반환한다.
    /// </summary>
    public bool Step(Model model, double dt)
    {
        if (IsFinished(model))
        {
            MoveStraight(model, dt);
            return false;
        }

        return MoveToWaypoint(model, dt);
    }

    private bool MoveToWaypoint(Model model, double dt)
    {
        var target = model.Waypoints[_waypointIndex];
        var targetPos = new XYZPos(target.X, target.Y, target.Z);

        // ── 1. 방향 계산 ──

        // 현재 위치 → 목표 웨이포인트 방위각
        double targetYaw = GeoUtil.Bearing(model.Pos, targetPos);

        // 선회율 제한 적용 → 실제 다음 Yaw (한 틱에 최대 TurnRate*dt 만큼만 회전)
        double yawNext = CalcYawNext(model.Pose.Yaw, targetYaw, TurnRate, dt);

        // 목표점 향한 고각
        double targetPitch = CalcPitchToTarget(model.Pos, targetPos);

        // pitch 변화율 제한 적용 (AltRate → 각속도로 변환: altRate / speed = rad/s 근사)
        double pitchNext = CalcPitchNext(model.Pose.Pitch, targetPitch, AltRate, model.Speed, dt);

        // ── 2. 위치 이동 (Yaw + Pitch 반영) ──
        // Speed * dt 중 cos(pitch)는 수평, sin(pitch)는 수직 성분

        double moveDist = model.Speed * dt;
        var nextPos = GeoUtil.NextPosition(model.Pos, yawNext, pitchNext, moveDist);

        // ── 3. 자세 업데이트 ──

        model.Pose = new Pose(yawNext, pitchNext, 0.0);
        model.Pos = nextPos;

        // ── 4. Fly-Over 통과 판정 ──
        // 이전 위치 → 웨이포인트 벡터와 현재 위치 → 웨이포인트 벡터의
        // 관계를 내적으로 판정. 웨이포인트를 지나쳤으면(내적 ≤ 0) 통과로 간주.
        bool passed = HasPassedWaypoint(model.Pos, targetPos, yawNext);

        if (passed)
        {
            _waypointIndex++;

            // 웨이포인트에 속도가 지정되어 있으면 적용
            if (target.Speed > 0.0)
            {
                model.Speed = target.Speed;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 비행체의 현재 진행 방향(yaw) 기준으로, 웨이포인트가 뒤쪽에 있으면 통과한 것으로 판정.
    /// 진행 방향 단위벡터와 (현재위치→웨이포인트) 벡터의 내적이 0 이하이면 통과.
    /// </summary>
    private static bool HasPassedWaypoint(in XYZPos currentPos, in XYZPos waypointPos, double yaw)
    {
        // 진행 방향 단위벡터 (ENU: X=동, Y=북)
        double yawRad = GeoUtil.DegToRad(yaw);
        double fwdX = Math.Sin(yawRad);
        double fwdY = Math.Cos(yawRad);

        // 현재 위치 → 웨이포인트 벡터
        double toWpX = waypointPos.X - currentPos.X;
        double toWpY = waypointPos.Y - currentPos.Y;

        // 내적: 양수면 아직 앞쪽, 0 이하면 옆이나 뒤 → 통과
        double dot = fwdX * toWpX + fwdY * toWpY;
        return dot <= 0.0;
    }

    private static void MoveStraight(Model model, double dt)
    {
        model.Pos = GeoUtil.NextPosition(model.Pos, model.Pose.Yaw, model.Pose.Pitch, model.Speed * dt);
    }

    // ── 보조 계산 ──

    /// <summary>
    /// 선회율 제한을 적용하여 다음 Yaw를 계산한다.
    /// 최단 회전 방향으로, 최대 turnRate * dt만큼 회전.
    /// </summary>
    private static double CalcYawNext(double currentYaw, double targetYaw, double turnRate, double dt)
    {
        double diff = NormalizeAngle(targetYaw - currentYaw);
        double maxTurn = turnRate * dt;

        if (Math.Abs(diff) <= maxTurn)
            return NormalizeYaw(targetYaw);

        double direction = diff > 0 ? 1.0 : -1.0;
        return NormalizeYaw(currentYaw + direction * maxTurn);
    }

    /// <summary>
    /// pitch 변화율 제한을 적용하여 다음 Pitch를 계산한다.
    /// AltRate(m/s)를 현재 속도 기반 각속도(deg/s)로 변환하여 제한.
    /// </summary>
    private static double CalcPitchNext(double currentPitch, double targetPitch, double altRate, double speed, double dt)
    {
        // altRate(m/s)를 pitch 각속도(deg/s)로 근사 변환
        // sin(pitchRate) ≈ altRate / speed → pitchRate ≈ asin(altRate / speed)
        double pitchRateDeg = (speed > 0.001)
            ? GeoUtil.RadToDeg(Math.Asin(Math.Min(altRate / speed, 1.0)))
            : 90.0;

        double diff = targetPitch - currentPitch;
        double maxChange = pitchRateDeg * dt;

        if (Math.Abs(diff) <= maxChange)
            return targetPitch;

        double direction = diff > 0 ? 1.0 : -1.0;
        return currentPitch + direction * maxChange;
    }

    /// <summary>
    /// 현재 위치에서 목표 지점을 향한 고각(Pitch)을 계산한다.
    /// </summary>
    private static double CalcPitchToTarget(in XYZPos from, in XYZPos to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;
        double dist2D = Math.Sqrt(dx * dx + dy * dy);
        return GeoUtil.RadToDeg(Math.Atan2(dz, dist2D));
    }

    private static double NormalizeAngle(double angle)
    {
        angle %= 360.0;
        if (angle > 180.0) angle -= 360.0;
        if (angle < -180.0) angle += 360.0;
        return angle;
    }

    private static double NormalizeYaw(double yaw)
    {
        return ((yaw % 360.0) + 360.0) % 360.0;
    }
}

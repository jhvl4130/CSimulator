using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 함포 모델. FCS 명령에 따라 조준 방향으로 탄환을 생성하고,
/// 내부에서 궤적 보간 및 충돌 판정을 일괄 처리한다.
/// </summary>
public class Gun : Model
{
    private class BulletData
    {
        public List<BallisticState> Trajectory = new();
        public int Cursor;
        public XYZPos Pos;
        public Model? Target;
        public int TargetId;
        public double FireTime;
    }

    private readonly List<BulletData> _bullets = new();

    /// <summary>
    /// 발사율 (발/분)
    /// </summary>
    public double Rpm { get; set; } = 4500.0;

    /// <summary>
    /// 탄속 (m/s)
    /// </summary>
    public double BulletSpeed { get; set; } = 1000.0;

    /// <summary>
    /// 탄환 파워
    /// </summary>
    public double BulletPower { get; set; } = 10.0;

    /// <summary>
    /// 잔여 탄약수
    /// </summary>
    public int Ammo { get; set; } = 1000;

    /// <summary>
    /// 선회 속도 (도/초)
    /// </summary>
    public double PoseTurnRate { get; set; } = 60.0;

    /// <summary>
    /// 탄종
    /// </summary>
    public string BulletType { get; set; } = "default";

    /// <summary>
    /// FCS 참조 (생성 시 주입)
    /// </summary>
    public Model? Fcs { get; set; }

    // 구동 상태 
    private double _cmdAzimuth;
    private double _cmdElevation;
    private double _curAzimuth;
    private double _curElevation;
    private double _prevAzimuth;
    private double _prevElevation;

    // 사격 상태
    private bool _isFiring;
    private double _lastFireTime;
    private int _totalFired;
    private int _currentTargetId;          // 260415 현재 사격 대상 InputId
    private string _currentTargetTag = ""; // 260415 현재 사격 대상 Tag

    /// <summary>
    /// 현재 틱의 발사 여부(0/1) CIWS.csv 콜백에서 읽음
    /// </summary>
    // 260415 0/1 대신 0 또는 사격 대상 InputId 기록으로 변경
    // public bool LastFireFlag { get; private set; }
    public int LastFireTargetId { get; private set; }
    // 260415 사격 중이면 대상 Tag, 아니면 빈 문자열
    public string LastFireTargetTag { get; private set; } = "";

    /// <summary>
    /// 발사 간격 (초)
    /// </summary>
    private double FireInterval => 60.0 / Rpm;

    /// <summary>
    /// 구동 주기 (초) 100Hz
    /// </summary>
    private const double DrivePeriod = 0.01;

    /// <summary>
    /// 조준 허용 오차 (도)
    /// </summary>
    private const double AimTolerance = 0.2;

    /// <summary>
    /// 충돌 검사 마진 (초). FireTime 기준으로 targetFireTime ± 이 값 범위의 탄환만 검사.
    /// </summary>
    private const double CollisionMarginSec = 1.0;

    /// <summary>
    /// 다음 탄환 ID
    /// </summary>
    private int _nextBulletId;

    public Gun(int id) : base(id)
    {
        Class = ModelClass.Weapon;
        Type = MtGun;
        Name = $"Gun-{id}";
    }

    public void SetBulletIdStart(int startId)
    {
        _nextBulletId = startId;
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.WaitStart;
        IsEnabled = true;
        IsStateChanged = false;
        _isFiring = false;
        _totalFired = 0;
        _currentTargetId = 0;
        _currentTargetTag = "";
        LastFireTargetId = 0;
        LastFireTargetTag = "";
        _bullets.Clear();
        return TInfinite;
    }

    public override double IntTrans(double t)
    {
        if (Phase != PhaseType.Run || !IsEnabled)
            return TInfinite;

        // PoseTurnRate 기반 조준각 갱신
        UpdatePose(DrivePeriod);

        // 조준 완료 + 사격 on + 탄약 있으면 발사 (RPM 간격 준수)
        bool onTarget = IsOnTarget();
        if (_isFiring && onTarget && Ammo > 0 && (t - _lastFireTime) >= FireInterval)
        {
            FireBullet(t);
            Ammo--;
            _totalFired++;
            _lastFireTime = t;
        }

        // 탄환 일괄 갱신: 위치 보간 + 할당 표적 충돌 검사
        UpdateBullets(t);

        // DriveResult 피드백 (200Hz)
        FireStatus fireStatus = !_isFiring ? FireStatus.Idle : (onTarget ? FireStatus.Firing : FireStatus.Slewing);
        if (Ammo <= 0 && _isFiring) fireStatus = FireStatus.AmmoOut;

        double azVel = (_curAzimuth - _prevAzimuth) / DrivePeriod;
        double elVel = (_curElevation - _prevElevation) / DrivePeriod;

        if (Fcs is not null)
        {
            Engine!.SendEvent(Fcs, new DriveResultEvent(
                _curAzimuth, _curElevation, azVel, elVel,
                _totalFired, Ammo, fireStatus));
        }

        bool poseChanged = (_curAzimuth != _prevAzimuth || _curElevation != _prevElevation);
        if (poseChanged)
        {
            Pose = new Pose(_curAzimuth, _curElevation, 0.0);
        }
        _prevAzimuth = _curAzimuth;
        _prevElevation = _curElevation;

        // CIWS.csv 출력 트리거: 자세 변화 또는 사격 중
        // 260415 사격 중이면 매 틱 state change 발생 → 다운샘플링으로 10Hz 출력에 타겟ID/Tag 노출
        bool firingNow = _isFiring && onTarget && Ammo > 0;
        if (poseChanged || firingNow)
        {
            // LastFireFlag = fireOccurred;
            LastFireTargetId = firingNow ? _currentTargetId : 0;
            LastFireTargetTag = firingNow ? _currentTargetTag : "";
            IsStateChanged = true;
        }

        // 탄약 소진 시 사격 중단
        if (Ammo <= 0 && _isFiring)
        {
            _isFiring = false;
            Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] Ammo depleted\n");
        }

        return DrivePeriod;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        switch (ev)
        {
            case DriveEvent drive:
                _cmdAzimuth = drive.Azimuth;
                _cmdElevation = drive.Elevation;
                if (Phase == PhaseType.WaitStart)
                {
                    Phase = PhaseType.Run;
                    _prevAzimuth = _curAzimuth;
                    _prevElevation = _curElevation;
                    return DrivePeriod;
                }
                return TContinue;

            case FireEvent fire:
                _isFiring = (fire.Cmd == FireCmd.On);
                _currentTargetId = _isFiring ? fire.TargetId : 0;      // 260415
                _currentTargetTag = _isFiring ? fire.TargetTag : "";   // 260415
                if (_isFiring)
                {
                    _lastFireTime = t - FireInterval; // 즉시 발사 가능
                    if (Phase == PhaseType.WaitStart)
                    {
                        Phase = PhaseType.Run;
                        return DrivePeriod;
                    }
                }
                Logger.Dbg(DbgFlag.Init,
                    $"{t:F6} [{Name}] Fire {fire.Cmd}\n");
                return TContinue;
        }

        return TContinue;
    }

    // 자세 계산

    private void UpdatePose(double dt)
    {
        double maxDelta = PoseTurnRate * dt;

        double azDiff = NormalizeAngle(_cmdAzimuth - _curAzimuth);
        double elDiff = _cmdElevation - _curElevation;

        _curAzimuth += Clamp(azDiff, -maxDelta, maxDelta);
        _curElevation += Clamp(elDiff, -maxDelta, maxDelta);
        _curAzimuth = NormalizeAngle360(_curAzimuth);

        Pose = new Pose(_curAzimuth, _curElevation, 0.0);
    }

    private bool IsOnTarget()
    {
        double azDiff = Math.Abs(NormalizeAngle(_cmdAzimuth - _curAzimuth));
        double elDiff = Math.Abs(_cmdElevation - _curElevation);
        return azDiff <= AimTolerance && elDiff <= AimTolerance;
    }

    /// <summary>
    /// -180 ~ 180으로 정규화
    /// </summary>
    /// <param name="angle"></param>
    /// <returns></returns>
    private static double NormalizeAngle(double angle)
    {
        while (angle > 180.0) angle -= 360.0;
        while (angle < -180.0) angle += 360.0;
        return angle;
    }

    /// <summary>
    /// 0 ~ 360으로 정규화
    /// </summary>
    /// <param name="angle"></param>
    /// <returns></returns>
    private static double NormalizeAngle360(double angle)
    {
        while (angle >= 360.0) angle -= 360.0;
        while (angle < 0.0) angle += 360.0;
        return angle;
    }

    private static double Clamp(double val, double min, double max)
    {
        return val < min ? min : (val > max ? max : val);
    }

    // ── 탄환 관리 ──

    private void FireBullet(double t)
    {
        double flightTime = 10.0;
        int numPoints = (int)(flightTime / MovePeriod) + 1;

        var trajectory = new List<BallisticState>(numPoints);
        for (int i = 0; i < numPoints; i++)
        {
            double dt = i * MovePeriod;
            double dist = BulletSpeed * dt;
            var pos = GeoUtil.NextPosition(Pos, _curAzimuth, _curElevation, dist);
            trajectory.Add(new BallisticState(t + dt, pos));
        }

        var target = Engine!.GetModel(_currentTargetId);
        var bullet = new BulletData
        {
            Trajectory = trajectory,
            Cursor = 0,
            Pos = trajectory[0].Pos,
            Target = target,
            TargetId = _currentTargetId,
            FireTime = t
        };
        _bullets.Add(bullet);

        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Fired bullet for target {_currentTargetId}, ammo={Ammo - 1}\n");
    }

    private void UpdateBullets(double t)
    {
        if (_bullets.Count == 0) return;

        // 표적 소실 → 전체 제거
        var tgt = _bullets[0].Target;
        if (tgt is null || !tgt.IsEnabled)
        {
            for (int i = 0; i < _bullets.Count; i++)
                SendBulletImpact(_bullets[i], false);
            _bullets.Clear();
            return;
        }

        // 앞쪽에서 만료 탄환 정리 (가장 오래된 것부터)
        while (_bullets.Count > 0 && t > _bullets[0].Trajectory[^1].Time)
        {
            SendBulletImpact(_bullets[0], false);
            _bullets.RemoveAt(0);
        }

        if (_bullets.Count == 0) return;

        // 이진 탐색: "지금 표적 근처에 있을 탄환"의 발사 시각 역산
        double distToTarget = GeoUtil.Distance(Pos, tgt.Pos);
        double targetFireTime = t - distToTarget / BulletSpeed;
        int lo = LowerBound(_bullets, targetFireTime - CollisionMarginSec);
        int hi = UpperBound(_bullets, targetFireTime + CollisionMarginSec);

        // [lo, hi] 구간만 보간 + AABB 충돌 검사
        bool hasSize = tgt.HalfX > 0.0 || tgt.HalfY > 0.0 || tgt.HalfZ > 0.0;
        if (hasSize)
        {
            for (int i = hi; i >= lo; i--)
            {
                var b = _bullets[i];
                var prevPos = InterpolateBullet(b, t - DrivePeriod);
                b.Pos = InterpolateBullet(b, t);

                if (CollisionDetection.IsSegmentAABB(
                    prevPos, b.Pos,
                    tgt.Pos, tgt.HalfX, tgt.HalfY, tgt.HalfZ))
                {
                    Logger.Dbg(DbgFlag.Collide,
                        $"{t:F6} [{Name}] bullet → [{tgt.Name}] Hit\n");
                    Engine!.SendEvent(tgt, new AttackEvent(BulletPower, Fcs));
                    SendBulletImpact(b, true);
                    _bullets.RemoveAt(i);
                }
            }
        }
    }

    private static int LowerBound(List<BulletData> bullets, double fireTime)
    {
        int lo = 0, hi = bullets.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (bullets[mid].FireTime < fireTime) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(List<BulletData> bullets, double fireTime)
    {
        int lo = 0, hi = bullets.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (bullets[mid].FireTime <= fireTime) lo = mid + 1;
            else hi = mid;
        }
        return lo - 1;
    }

    private static XYZPos InterpolateBullet(BulletData b, double t)
    {
        while (b.Cursor < b.Trajectory.Count - 2 && b.Trajectory[b.Cursor + 1].Time <= t)
            b.Cursor++;

        if (b.Cursor > 1)
        {
            b.Trajectory.RemoveRange(0, b.Cursor - 1);
            b.Cursor = 1;
        }

        if (b.Cursor >= b.Trajectory.Count - 1)
            return b.Trajectory[^1].Pos;

        var a = b.Trajectory[b.Cursor];
        var next = b.Trajectory[b.Cursor + 1];

        double dt = next.Time - a.Time;
        if (dt < 1e-12) return a.Pos;

        double alpha = Math.Clamp((t - a.Time) / dt, 0.0, 1.0);

        return new XYZPos(
            a.Pos.X + (next.Pos.X - a.Pos.X) * alpha,
            a.Pos.Y + (next.Pos.Y - a.Pos.Y) * alpha,
            a.Pos.Z + (next.Pos.Z - a.Pos.Z) * alpha);
    }

    private void SendBulletImpact(BulletData b, bool isHit)
    {
        if (Fcs is not null)
        {
            Engine!.SendEvent(Fcs, new BulletImpactEvent(b.Pos, isHit, b.TargetId));
        }
    }
}

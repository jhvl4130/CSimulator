using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 함포 모델. FCS 명령에 따라 조준 방향으로 Bullet을 생성한다.
/// Drive/Fire 분리, PoseTurnRate 기반 구동 역학, 200Hz DriveResult 피드백.
/// </summary>
public class Gun : Model
{
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
        // 260415 fireOccurred(per-tick 발사) → firingNow(사격 상태) 기반으로 CSV 트리거 변경
        if (_isFiring && onTarget && Ammo > 0 && (t - _lastFireTime) >= FireInterval)
        {
            // Test 탄환 객체 생성 비활성화 (O(bullets × targets) 충돌 검사 성능 이슈) — CIWS.csv Fire/Tag 기록은 상태 기반이라 영향 없음
            // FireBullet(t);
            Ammo--;
            _totalFired++;
            _lastFireTime = t;
        }

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

    // Bullet 생성

    private void FireBullet(double t)
    {
        int bulletId = _nextBulletId++;
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

        var bullet = new Bullet(bulletId)
        {
            IniPos = Pos,   // Gun의 ENU 좌표 (탄환 생성 시 발사 원점)
            BulletPower = BulletPower,
            Fcs = Fcs
        };
        bullet.SetTrajectory(trajectory);
        Engine!.AddRuntimeModel(bullet);

        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Fired Bullet-{bulletId}, ammo={Ammo - 1}\n");
    }
}

using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 함포 모델. FCS 명령에 따라 조준 방향으로 Bullet을 생성한다.
/// Drive/Fire 분리, SlewRate 기반 구동 역학, 200Hz DriveResult 피드백.
/// </summary>
public class Gun : Model
{
    /// <summary>발사율 (발/분).</summary>
    public double Rpm { get; set; } = 4500.0;

    /// <summary>탄속 (m/s).</summary>
    public double BulletSpeed { get; set; } = 1000.0;

    /// <summary>탄환 파워.</summary>
    public double BulletPower { get; set; } = 10.0;

    /// <summary>잔여 탄약수.</summary>
    public int Ammo { get; set; } = 1000;

    /// <summary>선회 속도 (도/초).</summary>
    public double SlewRate { get; set; } = 60.0;

    /// <summary>탄종.</summary>
    public string BulletType { get; set; } = "default";

    /// <summary>FCS 참조 (생성 시 주입).</summary>
    public Model? Fcs { get; set; }

    // ── 구동 상태 ──
    private double _cmdAzimuth;
    private double _cmdElevation;
    private double _curAzimuth;
    private double _curElevation;
    private double _prevAzimuth;
    private double _prevElevation;

    // ── 사격 상태 ──
    private bool _firing;
    private double _lastFireTime;
    private int _totalFired;

    /// <summary>직전 IntTrans 틱에서 발사가 발생했는지(CIWS.csv 트리거용).</summary>
    private bool _prevFireOutput;

    /// <summary>마지막으로 출력된 Fire 플래그(0/1). CIWS.csv 콜백에서 읽음.</summary>
    public bool LastFireFlag { get; private set; }

    /// <summary>발사 간격 (초).</summary>
    private double FireInterval => 60.0 / Rpm;

    /// <summary>구동 주기 (초). 200Hz.</summary>
    private const double DrivePeriod = 0.005;

    /// <summary>조준 허용 오차 (도).</summary>
    private const double AimTolerance = 0.5;

    /// <summary>다음 탄환 ID.</summary>
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
        _firing = false;
        _totalFired = 0;
        _prevFireOutput = false;
        LastFireFlag = false;
        return TInfinite;
    }

    public override double IntTrans(double t)
    {
        if (Phase != PhaseType.Run || !IsEnabled)
            return TInfinite;

        // SlewRate 기반 조준각 갱신
        UpdateSlew(DrivePeriod);

        // 조준 완료 + 사격 on + 탄약 있으면 발사 (RPM 간격 준수)
        bool onTarget = IsOnTarget();
        bool fireOccurred = false;
        if (_firing && onTarget && Ammo > 0 && (t - _lastFireTime) >= FireInterval)
        {
            // FireBullet(t);
            Ammo--;
            _totalFired++;
            _lastFireTime = t;
            fireOccurred = true;
        }

        // DriveResult 피드백 (200Hz)
        FireStatus fireStatus = !_firing ? FireStatus.Idle : (onTarget ? FireStatus.Firing : FireStatus.Slewing);
        if (Ammo <= 0 && _firing) fireStatus = FireStatus.AmmoOut;

        double azVel = (_curAzimuth - _prevAzimuth) / DrivePeriod;
        double elVel = (_curElevation - _prevElevation) / DrivePeriod;

        if (Fcs is not null)
        {
            Engine!.SendEvent(Fcs, new DriveResultEvent(
                _curAzimuth, _curElevation, azVel, elVel,
                _totalFired, Ammo, fireStatus));
        }

        if (_curAzimuth != _prevAzimuth || _curElevation != _prevElevation)
        {
            Pose = new Pose(_curAzimuth, _curElevation, 0.0);
        }
        _prevAzimuth = _curAzimuth;
        _prevElevation = _curElevation;

        // CIWS.csv 출력 트리거: 발사 발생 또는 1→0 전이
        if (fireOccurred)
        {
            LastFireFlag = true;
            IsStateChanged = true;
        }
        else if (_prevFireOutput)
        {
            LastFireFlag = false;
            IsStateChanged = true;
        }
        _prevFireOutput = fireOccurred;

        // 탄약 소진 시 사격 중단
        if (Ammo <= 0 && _firing)
        {
            _firing = false;
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
                _firing = (fire.Cmd == FireCmd.On);
                if (_firing)
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

    // ── Slew Dynamics ──

    private void UpdateSlew(double dt)
    {
        double maxDelta = SlewRate * dt;

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

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180.0) angle -= 360.0;
        while (angle < -180.0) angle += 360.0;
        return angle;
    }

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

    // ── Bullet 생성 ──

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
            BulletPower = BulletPower,
            Fcs = Fcs
        };
        bullet.SetTrajectory(trajectory);
        Engine!.AddRuntimeModel(bullet);

        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Fired Bullet-{bulletId}, ammo={Ammo - 1}\n");
    }
}

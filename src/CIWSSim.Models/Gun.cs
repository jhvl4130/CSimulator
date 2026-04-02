using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

/// <summary>
/// 함포 모델. FCS 명령에 따라 조준 방향으로 Bullet을 생성한다.
/// 선회속도(slew rate) 제한과 탄약 제한을 가진다.
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

    /// <summary>FCS 참조 (생성 시 주입).</summary>
    public Model? Fcs { get; set; }

    /// <summary>현재 조준 방위각 (도).</summary>
    private double _aimAzimuth;
    private double _aimElevation;

    /// <summary>현재 교전 표적.</summary>
    private Model? _target;

    /// <summary>발사 간격 (초).</summary>
    private double FireInterval => 60.0 / Rpm;

    /// <summary>다음 탄환 ID.</summary>
    private int _nextBulletId;

    /// <summary>발사 활성 여부.</summary>
    private bool _firing;

    public Gun(int id) : base(id)
    {
        Class = ModelClass.Weapon;
        Type = MtGun;
        Name = $"Gun-{id}";
    }

    /// <summary>Bullet ID 시작 번호 설정.</summary>
    public void SetBulletIdStart(int startId)
    {
        _nextBulletId = startId;
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        Phase = PhaseType.WaitStart;
        IsEnabled = true;
        _firing = false;
        return TInfinite;
    }

    public override double IntTrans(double t)
    {
        if (!_firing || Phase != PhaseType.Run || !IsEnabled)
            return TInfinite;

        if (Ammo <= 0)
        {
            _firing = false;
            Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] Ammo depleted\n");
            if (Fcs is not null)
                Engine!.SendEvent(Fcs, new StatusEvent("AmmoOut", $"{Name} ammo depleted"));
            return TInfinite;
        }

        // 표적이 이미 파괴된 경우 사격 중단
        if (_target is not null && !_target.IsEnabled)
        {
            _firing = false;
            return TInfinite;
        }

        // Bullet 생성: 현재 조준 방향으로 직선 궤적
        FireBullet(t);
        Ammo--;

        return FireInterval;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        if (ev is FireCmdEvent cmd)
        {
            _aimAzimuth = cmd.AimAzimuth;
            _aimElevation = cmd.AimElevation;
            _target = cmd.Target;
            _firing = true;
            Phase = PhaseType.Run;

            // Pose 업데이트
            Pose = new Pose(_aimAzimuth, _aimElevation, 0.0);

            Logger.Dbg(DbgFlag.Init,
                $"{t:F6} [{Name}] Fire cmd: Az={_aimAzimuth:F2} El={_aimElevation:F2}\n");

            return FireInterval;
        }

        return TContinue;
    }

    private void FireBullet(double t)
    {
        // 직선 궤적 생성: Gun 위치에서 조준 방향으로 BulletSpeed로 비행
        int bulletId = _nextBulletId++;
        double flightTime = 10.0; // 최대 비행 시간
        int numPoints = (int)(flightTime / MovePeriod) + 1;

        var trajectory = new List<BulletPoint>(numPoints);
        for (int i = 0; i < numPoints; i++)
        {
            double dt = i * MovePeriod;
            double dist = BulletSpeed * dt;
            var pos = GeoUtil.NextPosition(Pos, _aimAzimuth, _aimElevation, dist);
            trajectory.Add(new BulletPoint(t + dt, pos));
        }

        var bullet = new Bullet(bulletId) { BulletPower = BulletPower };
        bullet.SetTrajectory(trajectory);
        Engine!.AddRuntimeModel(bullet);

        Logger.Dbg(DbgFlag.Collide,
            $"{t:F6} [{Name}] Fired Bullet-{bulletId}, ammo={Ammo - 1}\n");
    }
}

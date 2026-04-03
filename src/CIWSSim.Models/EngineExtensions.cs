using CIWSSimulator.Core;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;

namespace CIWSSimulator.Models;

public static class EngineExtensions
{
    // ── Airplane ──

    public static void AddAirplane(this Engine engine, int id,
        double x, double y, double z, double speed,
        double azimuth, double elevation, double startT,
        double sizeX = 0.0, double sizeY = 0.0, double sizeZ = 0.0)
    {
        var model = new Airplane(id)
        {
            IniPos = new XYZPos(x, y, z),
            IniSpeed = speed,
            IniAzimuth = azimuth,
            IniElevation = elevation,
            StartT = startT,
            HalfX = sizeX / 2.0,
            HalfY = sizeY / 2.0,
            HalfZ = sizeZ / 2.0
        };
        engine.RegisterModel(model);
    }

    /// <summary>LLH 좌표로 Airplane 추가. 내부에서 ENU 변환.</summary>
    public static void AddAirplane(this Engine engine, int id,
        LLHPos llh, double speed,
        double azimuth, double elevation, double startT,
        double sizeX = 0.0, double sizeY = 0.0, double sizeZ = 0.0)
    {
        var enu = GeoUtil.LlaToEnu(llh, engine.Origin);
        engine.AddAirplane(id, enu.X, enu.Y, enu.Z, speed, azimuth, elevation, startT,
            sizeX, sizeY, sizeZ);
    }

    // ── Waypoint ──

    /// <summary>LLH 좌표로 웨이포인트 추가.</summary>
    public static void AddWaypointLLH(this Engine engine, int id,
        LLHPos llh, double speed)
    {
        var enu = GeoUtil.LlaToEnu(llh, engine.Origin);
        var model = engine.GetModel(id);
        if (model is null)
        {
            Logger.Warn($"AddWaypointLLH: No model for ID '{id}'\n");
            return;
        }
        model.AddWaypoint(new XYZWayp(enu.X, enu.Y, enu.Z, speed));
    }

    // ── AssetZone (반구 영역) ──

    /// <summary>
    /// ENU 좌표로 반구 방어 영역 추가. center가 반구 바닥 중심, radius가 반경.
    /// </summary>
    public static void AddAssetZone(this Engine engine, int id,
        double x, double y, double z, double radius)
    {
        var model = new AssetZone(id)
        {
            IniPos = new XYZPos(x, y, z),
            Radius = radius
        };
        engine.RegisterModel(model);
    }

    /// <summary>LLH 좌표로 반구 방어 영역 추가.</summary>
    public static void AddAssetZone(this Engine engine, int id,
        LLHPos centerLlh, double radius)
    {
        var enu = GeoUtil.LlaToEnu(centerLlh, engine.Origin);
        engine.AddAssetZone(id, enu.X, enu.Y, enu.Z, radius);
    }

    // ── Launcher ──

    public static void AddLauncher(this Engine engine, int id,
        double x, double y, double z, double speed,
        double azimuth, double elevation, double startT)
    {
        var model = new Launcher(id)
        {
            IniPos = new XYZPos(x, y, z),
            IniSpeed = speed,
            IniAzimuth = azimuth,
            IniElevation = elevation,
            StartT = startT
        };
        engine.RegisterModel(model);
    }

    // ── Bullet ──

    /// <summary>
    /// 궤적 리스트로 Bullet 추가. 시뮬레이션 중 런타임 등록.
    /// </summary>
    public static void AddBullet(this Engine engine, int id,
        List<BulletPoint> trajectory, double power = 10.0)
    {
        var model = new Bullet(id) { BulletPower = power };
        model.SetTrajectory(trajectory);
        engine.AddRuntimeModel(model);
    }

    /// <summary>LLH 좌표로 Launcher 추가.</summary>
    public static void AddLauncher(this Engine engine, int id,
        LLHPos posLlh, double speed,
        double azimuth, double elevation, double startT)
    {
        var pos = GeoUtil.LlaToEnu(posLlh, engine.Origin);
        engine.AddLauncher(id, pos.X, pos.Y, pos.Z, speed,
            azimuth, elevation, startT);
    }

    // ── SearchRadar ──

    public static SearchRadar AddSearchRadar(this Engine engine, int id,
        LLHPos posLlh, double detectRange, double detectPeriod)
    {
        var enu = GeoUtil.LlaToEnu(posLlh, engine.Origin);
        var model = new SearchRadar(id)
        {
            IniPos = enu,
            DetectRange = detectRange,
            DetectPeriod = detectPeriod
        };
        engine.RegisterModel(model);
        return model;
    }

    // ── C2Control ──

    public static C2Control AddC2Control(this Engine engine, int id,
        string? eventLogPath = "event_log.csv")
    {
        var model = new C2Control(id)
        {
            EventLogPath = eventLogPath
        };
        engine.RegisterModel(model);
        return model;
    }

    // ── CIWS 세트 (FCS + TrackRadar + EOTS + Gun) ──

    /// <summary>
    /// CIWS 1세트를 생성하고 상호 참조를 연결한다.
    /// 반환: (FCS, TrackRadar, EOTS, Gun)
    /// </summary>
    public static (FCS fcs, TrackRadar trackRadar, Eots eots, Gun gun) AddCIWS(
        this Engine engine, int ciwsId, LLHPos posLlh,
        double trackPeriod, double eotsTrackPeriod,
        double fireRange,
        double rpm, double bulletSpeed, double bulletPower,
        int ammo, double slewRate,
        C2Control c2)
    {
        var enu = GeoUtil.LlaToEnu(posLlh, engine.Origin);

        // ID 규칙: CIWS ID 기준으로 하위 모델 ID 생성
        int fcsId = ciwsId;
        int trackRadarId = ciwsId + 1;
        int eotsId = ciwsId + 2;
        int gunId = ciwsId + 3;

        var fcs = new FCS(fcsId)
        {
            IniPos = enu,
            FireRange = fireRange,
            CiwsId = ciwsId
        };

        var trackRadar = new TrackRadar(trackRadarId)
        {
            IniPos = enu,
            TrackPeriod = trackPeriod
        };

        var eots = new Eots(eotsId)
        {
            IniPos = enu
        };

        var gun = new Gun(gunId)
        {
            IniPos = enu,
            Rpm = rpm,
            BulletSpeed = bulletSpeed,
            BulletPower = bulletPower,
            Ammo = ammo,
            SlewRate = slewRate
        };
        gun.SetBulletIdStart(ciwsId * 10000);

        // 상호 참조 연결
        fcs.TrackRadar = trackRadar;
        fcs.EotsModel = eots;
        fcs.GunModel = gun;
        fcs.C2 = c2;

        trackRadar.Fcs = fcs;
        eots.Fcs = fcs;
        gun.Fcs = fcs;

        // Engine 등록
        engine.RegisterModel(fcs);
        engine.RegisterModel(trackRadar);
        engine.RegisterModel(eots);
        engine.RegisterModel(gun);

        // C2에 FCS 등록
        c2.FcsList.Add(fcs);

        return (fcs, trackRadar, eots, gun);
    }

    // ── AssetZone (C2 참조 포함) ──

    /// <summary>LLH 좌표로 반구 방어 영역 추가 (C2 참조 포함).</summary>
    public static void AddAssetZone(this Engine engine, int id,
        LLHPos centerLlh, double radius, C2Control? c2)
    {
        var enu = GeoUtil.LlaToEnu(centerLlh, engine.Origin);
        var model = new AssetZone(id)
        {
            IniPos = enu,
            Radius = radius,
            C2 = c2
        };
        engine.RegisterModel(model);
    }
}

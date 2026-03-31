using CIWSSim.Core;
using CIWSSim.Core.Geometry;

namespace CIWSSim.Models;

public static class EngineExtensions
{
    // ── Airplane ──

    public static void AddAirplane(this Engine engine, int id,
        double x, double y, double z, double speed,
        double azimuth, double elevation, double startT)
    {
        var model = new Airplane(id)
        {
            IniPos = new XYZPos(x, y, z),
            IniSpeed = speed,
            IniAzimuth = azimuth,
            IniElevation = elevation,
            StartT = startT
        };
        engine.RegisterModel(model);
    }

    /// <summary>LLH 좌표로 Airplane 추가. 내부에서 ENU 변환.</summary>
    public static void AddAirplane(this Engine engine, int id,
        LLHPos llh, double speed,
        double azimuth, double elevation, double startT)
    {
        var enu = GeoUtil.LlaToEnu(llh, engine.Origin);
        engine.AddAirplane(id, enu.X, enu.Y, enu.Z, speed, azimuth, elevation, startT);
    }

    // ── Waypoint ──

    /// <summary>LLH 좌표로 웨이포인트 추가.</summary>
    public static void AddWaypointLLH(this Engine engine, int id,
        LLHPos llh, double speed)
    {
        var enu = GeoUtil.LlaToEnu(llh, engine.Origin);
        engine.AddWaypoint(id, enu.X, enu.Y, enu.Z, speed);
    }

    // ── Asset (직육면체: 센터 LLH + 크기) ──

    /// <summary>
    /// 센터 LLH + 길이(X)/넓이(Y)/높이(Z)로 직육면체 Asset 추가.
    /// 길이·넓이는 ENU 기준 미터 단위.
    /// </summary>
    public static void AddAssetBox(this Engine engine, int id,
        LLHPos centerLlh, double lengthX, double widthY, double heightZ)
    {
        var c = GeoUtil.LlaToEnu(centerLlh, engine.Origin);
        var hx = lengthX / 2.0;
        var hy = widthY / 2.0;

        var building = new Building
        {
            Polygon = new List<XYPos>
            {
                new(c.X - hx, c.Y - hy),
                new(c.X + hx, c.Y - hy),
                new(c.X + hx, c.Y + hy),
                new(c.X - hx, c.Y + hy),
            },
            Bottom = c.Z,
            Top = c.Z + heightZ
        };
        engine.AddAsset(id, building);
    }

    // ── Asset (Building: 사각형 꼭짓점 LLH + Top/Bottom) ──

    /// <summary>
    /// 사각형 대각선 두 꼭짓점(SW, NE)의 LLH + 해발고도(Bottom) + 건물높이(Top)로 Building Asset 추가.
    /// </summary>
    public static void AddAssetRect(this Engine engine, int id,
        LLHPos swLlh, LLHPos neLlh, double bottom, double top)
    {
        var sw = GeoUtil.LlaToEnu(swLlh, engine.Origin);
        var ne = GeoUtil.LlaToEnu(neLlh, engine.Origin);

        var building = new Building
        {
            Polygon = new List<XYPos>
            {
                new(sw.X, sw.Y),
                new(ne.X, sw.Y),
                new(ne.X, ne.Y),
                new(sw.X, ne.Y),
            },
            Bottom = bottom,
            Top = top
        };
        engine.AddAsset(id, building);
    }

    // ── Asset (기존 Building 직접 전달) ──

    public static void AddAsset(this Engine engine, int id, Building building)
    {
        var model = new Asset(id);
        model.SetBuilding(building);
        engine.RegisterCollidable(model);
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
        int startRktId, int rktNum, double period,
        double x, double y, double z, double speed,
        double gipX, double gipY, double gipZ,
        double azimuth, double elevation, double startT)
    {
        var model = new Launcher(id)
        {
            StartRktId = startRktId,
            RktNum = rktNum,
            FirePeriod = period,
            IniPos = new XYZPos(x, y, z),
            IniSpeed = speed,
            Gip = new XYZPos(gipX, gipY, gipZ),
            IniAzimuth = azimuth,
            IniElevation = elevation,
            StartT = startT
        };
        engine.RegisterModel(model);
    }

    /// <summary>LLH 좌표로 Launcher 추가.</summary>
    public static void AddLauncher(this Engine engine, int id,
        int startRktId, int rktNum, double period,
        LLHPos posLlh, double speed, LLHPos gipLlh,
        double azimuth, double elevation, double startT)
    {
        var pos = GeoUtil.LlaToEnu(posLlh, engine.Origin);
        var gip = GeoUtil.LlaToEnu(gipLlh, engine.Origin);
        engine.AddLauncher(id, startRktId, rktNum, period,
            pos.X, pos.Y, pos.Z, speed,
            gip.X, gip.Y, gip.Z,
            azimuth, elevation, startT);
    }
}

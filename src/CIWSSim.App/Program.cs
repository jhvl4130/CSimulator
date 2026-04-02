using CIWSSimulator.App;
using CIWSSimulator.Core;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using CIWSSimulator.Models;

// ── JSON 시나리오 로드 ──
var jsonPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "scenario.json");

var config = FileIO.LoadJson<ScenarioConfig>(jsonPath);
if (config is null)
{
    Logger.Err($"시나리오 파일을 읽을 수 없습니다: {jsonPath}\n");
    return;
}

var engine = new Engine();

// ── Origin 설정 ──
engine.Origin = new LLHPos(config.Origin.Lat, config.Origin.Lon, config.Origin.Alt);

// ── C2Control 등록 ──
C2Control? c2 = null;
if (config.C2 is not null)
{
    c2 = engine.AddC2Control(config.C2.Id);
}

// ── CIWS 세트 등록 ──
foreach (var ciws in config.Ciws)
{
    if (c2 is null)
    {
        Logger.Err("CIWS requires C2 to be configured\n");
        return;
    }

    engine.AddCIWS(ciws.Id,
        new LLHPos(ciws.Position.Lat, ciws.Position.Lon, ciws.Position.Alt),
        ciws.TrackRadar.TrackPeriod,
        ciws.Eots.TrackPeriod,
        ciws.Fcs.FireRange,
        ciws.Gun.Rpm, ciws.Gun.BulletSpeed, ciws.Gun.BulletPower,
        ciws.Gun.Ammo, ciws.Gun.SlewRate,
        c2);
}

// ── SearchRadar 등록 ──
if (config.SearchRadar is not null)
{
    var sr = engine.AddSearchRadar(config.SearchRadar.Id,
        new LLHPos(config.SearchRadar.Position.Lat,
                   config.SearchRadar.Position.Lon,
                   config.SearchRadar.Position.Alt),
        config.SearchRadar.DetectRange,
        config.SearchRadar.DetectPeriod);
    sr.C2 = c2;
}

// ── AssetZone 등록 ──
foreach (var az in config.AssetZones)
{
    engine.AddAssetZone(az.Id,
        new LLHPos(az.Position.Lat, az.Position.Lon, az.Position.Alt),
        az.Radius, c2);
}

// ── Airplane 등록 ──
foreach (var ap in config.Airplanes)
{
    engine.AddAirplane(ap.Id,
        new LLHPos(ap.Position.Lat, ap.Position.Lon, ap.Position.Alt),
        ap.Speed, ap.Azimuth, ap.Elevation, ap.StartT,
        ap.Size.LengthX, ap.Size.WidthY, ap.Size.HeightZ);

    foreach (var wp in ap.Waypoints)
    {
        engine.AddWaypointLLH(ap.Id, new LLHPos(wp.Lat, wp.Lon, wp.Alt), wp.Speed);
    }
}

// ── Building 등록 ──
foreach (var bd in config.Buildings)
{
    engine.AddAssetRect(bd.Id,
        new LLHPos(bd.Sw.Lat, bd.Sw.Lon, 0.0),
        new LLHPos(bd.Ne.Lat, bd.Ne.Lon, 0.0),
        bd.Bottom, bd.Top);
}

// ── Launcher 등록 ──
foreach (var lc in config.Launchers)
{
    engine.AddLauncher(lc.Id, lc.StartRktId, lc.RktNum, lc.FirePeriod,
        new LLHPos(lc.Position.Lat, lc.Position.Lon, lc.Position.Alt),
        lc.Speed,
        new LLHPos(lc.Gip.Lat, lc.Gip.Lon, lc.Gip.Alt),
        lc.Azimuth, lc.Elevation, lc.StartT);
}

// ── 시뮬레이션 실행 ──
engine.Start(config.SimEndTime);

// ── C2 이벤트 로그 종료 ──
c2?.Dispose();

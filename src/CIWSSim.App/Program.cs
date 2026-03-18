using CIWSSim.App;
using CIWSSim.Core;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;
using CIWSSim.Models;

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

// ── Airplane 등록 ──
foreach (var ap in config.Airplanes)
{
    engine.AddAirplane(ap.Id,
        new LLHPos(ap.Position.Lat, ap.Position.Lon, ap.Position.Alt),
        ap.Speed, ap.Azimuth, ap.Elevation, ap.StartT);

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

// ── 시뮬레이션 실행 ──
engine.Start(config.SimEndTime);

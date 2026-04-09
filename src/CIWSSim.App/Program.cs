using System.Text;
using CIWSSimulator.App;
using CIWSSimulator.Core;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using CIWSSimulator.Models;

// ── input.json 로드 ──
var jsonPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "input.json");

var input = FileIO.LoadJson<InputConfig>(jsonPath);
if (input is null)
{
    Logger.Err($"입력 파일을 읽을 수 없습니다: {jsonPath}\n");
    return;
}

// ── tag별 데이터 분류 ──
var ciwsItems = input.Records
    .FirstOrDefault(r => r.Tag == "CombatPlatform.Ciws")?.Items ?? new();
var targetItems = input.Records
    .FirstOrDefault(r => r.Tag == "CombatPlatform.Target.Aircraft")?.Items ?? new();
var areaRecord = input.Records
    .FirstOrDefault(r => r.Tag == "Area");

// ── Origin 결정: worldMapLLH → Area tag → CIWS 중심점 ──
LLHPos origin;
if (input.WorldMapLLH is not null)
{
    origin = new LLHPos(input.WorldMapLLH.Latitude, input.WorldMapLLH.Longitude, 0.0);
}
else if (areaRecord is not null && areaRecord.Items.Count > 0)
{
    var a = areaRecord.Items[0].Position;
    origin = new LLHPos(a.Latitude, a.Longitude, a.Height);
}
else if (ciwsItems.Count > 0)
{
    double latSum = 0, lonSum = 0, altSum = 0;
    foreach (var c in ciwsItems)
    {
        latSum += c.Position.Latitude;
        lonSum += c.Position.Longitude;
        altSum += c.Position.Height;
    }
    origin = new LLHPos(latSum / ciwsItems.Count, lonSum / ciwsItems.Count, altSum / ciwsItems.Count);
}
else
{
    Logger.Err("Origin을 결정할 수 없습니다.\n");
    return;
}

// ── 프로토타입 상수 ──
const double SimEndTime = 180.0;       // 3분
const double DefaultSpeed = 200.0;     // m/s
const double DetectRange = 10000.0;    // 탐지 거리 (m)
const double DetectPeriod = 1.0;       // 탐지 주기 (초)
const double TrackPeriod = 0.04;       // 추적 주기 (25Hz)
const double FireRange = 1500.0;       // 사격 거리 (m)
const double AssetRadius = 2000.0;     // 방어 영역 반경 (m)

var engine = new Engine();
engine.Origin = origin;

// ── CSV 스트리밍 출력 설정 ──
using var csvWriter = new StreamWriter("output.csv", false, Encoding.UTF8);
csvWriter.WriteLine(string.Join(',', StateRecord.CsvHeader));

engine.OnModelTransitioned = (time, model) =>
{
    if (string.IsNullOrEmpty(model.Tag)) return;
    var record = new StateRecord(time, model.Tag, model.Id, model.Pos, model.Pose);
    csvWriter.WriteLine(string.Join(',', record.ToCsvRow(engine.Origin)));
};

// ── C2Control 등록 ──
var c2 = engine.AddC2Control(500);

// ── CIWS 세트 등록 (input의 CIWS 위치 사용) ──
int ciwsBaseId = 300;
foreach (var ciws in ciwsItems)
{
    var pos = new LLHPos(ciws.Position.Latitude, ciws.Position.Longitude, ciws.Position.Height);
    engine.AddCIWS(ciwsBaseId, pos,
        TrackPeriod, FireRange,
        4500, 1000, 10, 1000, 60,
        c2);
    ciwsBaseId += 10;
}

// ── SearchRadar/AssetZone 위치: CIWS 중심점 ──
LLHPos siteCenter;
if (ciwsItems.Count > 0)
{
    double latSum = 0, lonSum = 0, altSum = 0;
    foreach (var c in ciwsItems)
    {
        latSum += c.Position.Latitude;
        lonSum += c.Position.Longitude;
        altSum += c.Position.Height;
    }
    siteCenter = new LLHPos(latSum / ciwsItems.Count, lonSum / ciwsItems.Count, altSum / ciwsItems.Count);
}
else
{
    siteCenter = origin;
}

var sr = engine.AddSearchRadar(100, siteCenter, DetectRange, DetectPeriod);
sr.C2 = c2;

engine.AddAssetZone(900, siteCenter, AssetRadius, c2);

// ── Airplane 등록 (input의 Target 데이터 사용) ──
int tgtBaseId = 1;
foreach (var tgt in targetItems)
{
    var pos = new LLHPos(tgt.Position.Latitude, tgt.Position.Longitude, tgt.Position.Height);
    double azimuth = tgt.Rotation.Yaw;
    double elevation = tgt.Rotation.Pitch;

    engine.AddAirplane(tgtBaseId, pos, DefaultSpeed, azimuth, elevation, 0.0);
    tgtBaseId++;
}

// ── 시뮬레이션 실행 ──
engine.Start(SimEndTime);

// ── C2 이벤트 로그 종료 ──
c2.Dispose();

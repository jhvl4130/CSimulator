using System.Text;
using CIWSSimulator.Core;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using CIWSSimulator.Models;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.App;

public class SimulationBuilder
{
    // 상수
    private const double SimEndTime = 120.0;   // Test 시뮬레이션 시간 단축 (원복 시 180.0)
    private const double DefaultSpeed = 100.0;
    private const double DetectRange = 10000.0;
    private const double DetectPeriod = 1.0;
    private const double TrackPeriod = 0.04;
    private const double FireRange = 10000.0;
    private const double AssetRadius = 500.0;

    // Airplane AABB (world-axis aligned) — x 좌우, y 앞뒤, z 상하
    private const double AirplaneSizeX = 2.0;
    private const double AirplaneSizeY = 4.5;
    private const double AirplaneSizeZ = 0.4;

    /// <summary>
    /// 모든 입출력 파일이 위치하는 디렉토리
    /// </summary>
    private const string FileDir = @"D:\CIWSSimulator\File";

    private readonly InputConfig _input;
    private readonly Engine _engine = new();

    private TerrainMap? _terrain;
    private C2Control? _c2;
    private StreamWriter? _targetCsvWriter;
    private StreamWriter? _ciwsCsvWriter;
    private StreamWriter? _statusCsvWriter;
    private readonly Dictionary<int, long> _lastLogIndex = new();
    private readonly Dictionary<int, int> _lastTargetStatus = new();
    private readonly Dictionary<int, int> _lastGunFire = new();
    private double _pendingStatusTime = double.NaN;
    private string? _pendingStatusLine;

    public SimulationBuilder(string[] args)
    {
        Directory.CreateDirectory(FileDir);

        var jsonPath = args.Length > 0
            ? args[0]
            : Path.Combine(FileDir, "input.json");

        var input = FileIO.LoadJson<InputConfig>(jsonPath);
        if (input is null)
            throw new InvalidOperationException($"입력 파일을 읽을 수 없습니다: {jsonPath}");

        _input = input;
    }

    public void Run()
    {
        Build();

        _engine.Start(SimEndTime);

        FlushPendingStatus();

        _c2?.Dispose();
        _targetCsvWriter?.Dispose();
        _ciwsCsvWriter?.Dispose();
        _statusCsvWriter?.Dispose();
    }

    private void Build()
    {
        var ciwsItems = _input.Records
            .FirstOrDefault(r => r.Tag == "CombatPlatform.Ciws")?.Items ?? new();
        var targetItems = _input.Records
            .FirstOrDefault(r => r.Tag == "CombatPlatform.Target.Aircraft")?.Items ?? new();

        _engine.Origin = ResolveOrigin(ciwsItems);

        _terrain = LoadTerrain();

        SetupCsvOutput();

        _c2 = _engine.AddC2Control(500, Path.Combine(FileDir, "event_log.csv"));
        _c2.OnStatsChanged = WriteStatusRow;

        RegisterCiws(ciwsItems);

        var siteCenter = CalcSiteCenter(ciwsItems);
        var sr = _engine.AddSearchRadar(100, siteCenter, DetectRange, DetectPeriod);
        sr.C2 = _c2;
        ApplyTerrainToSearchRadar(sr);

        _engine.AddAssetZone(900, siteCenter, AssetRadius, _c2);

        RegisterTargets(targetItems);
        _c2.TotalTargets = targetItems.Count;
        WriteStatusRow(0.0);
    }

    private LLHPos ResolveOrigin(List<RecordItem> ciwsItems)
    {
        if (_input.WorldMapLLH is not null)
            return new LLHPos(_input.WorldMapLLH.Latitude, _input.WorldMapLLH.Longitude, 0.0);

        throw new InvalidOperationException("Origin을 결정할 수 없습니다.");
    }

    private TerrainMap? LoadTerrain()
    {
        if (_input.Terrain is null || string.IsNullOrWhiteSpace(_input.Terrain.MetaPath))
            return null;

        string metaPath = Path.IsPathRooted(_input.Terrain.MetaPath)
            ? _input.Terrain.MetaPath
            : Path.Combine(FileDir, _input.Terrain.MetaPath);

        var terrain = TerrainMap.LoadFromMeta(metaPath);

        // origin 일치 검증 — 격자 좌표가 ENU 원점에 맞춰 만들어졌는지 확인
        const double OriginTolDeg = 1e-6; // ≈ 0.11m at 위도 37°
        double dLat = Math.Abs(terrain.OriginLlh.Lat - _engine.Origin.Lat);
        double dLon = Math.Abs(terrain.OriginLlh.Lon - _engine.Origin.Lon);
        if (dLat > OriginTolDeg || dLon > OriginTolDeg)
        {
            throw new InvalidOperationException(
                $"지형 메타의 originLlh({terrain.OriginLlh.Lat:F8}, {terrain.OriginLlh.Lon:F8})가 " +
                $"시뮬 Origin({_engine.Origin.Lat:F8}, {_engine.Origin.Lon:F8})과 일치하지 않습니다. " +
                "동일 origin으로 다시 변환하세요.");
        }

        return terrain;
    }

    private void ApplyTerrainToSearchRadar(SearchRadar sr)
    {
        if (_terrain is null || _input.Terrain is null) return;
        sr.Terrain = _terrain;
        sr.SampleStepM = _input.Terrain.SampleStepM;
        sr.UseEarthCurvature = _input.Terrain.UseEarthCurvature;
    }

    private void ApplyTerrainToTrackRadar(TrackRadar tr)
    {
        if (_terrain is null || _input.Terrain is null) return;
        tr.Terrain = _terrain;
        tr.SampleStepM = _input.Terrain.SampleStepM;
        tr.UseEarthCurvature = _input.Terrain.UseEarthCurvature;
        tr.LosLossTicks = _input.Terrain.LosLossTicks;
    }

    private void SetupCsvOutput()
    {
        _targetCsvWriter = new StreamWriter(Path.Combine(FileDir, "Target.csv"), false, Encoding.UTF8);
        _targetCsvWriter.WriteLine("Time,ID,Type,Status,Lat,Lon,Alt,Roll,Pitch,Yaw");

        _ciwsCsvWriter = new StreamWriter(Path.Combine(FileDir, "CIWS.csv"), false, Encoding.UTF8);
        _ciwsCsvWriter.WriteLine("Time,ID,Pitch,Yaw,Fire,Type,FireCount");

        _statusCsvWriter = new StreamWriter(Path.Combine(FileDir, "Status.csv"), false, Encoding.UTF8);
        _statusCsvWriter.WriteLine("Time,TotalTargets,Detected,InterceptSuccess,InterceptFail");

        _engine.OnModelTransitioned = (time, model) =>
        {
            if (!model.IsStateChanged) return;
            model.IsStateChanged = false;

            long logIndex = (long)Math.Round(time / OutputPeriod);
            double gridTime = logIndex * OutputPeriod;

            if (model.Class == ModelClass.Target && model is TargetBase target)
            {
                // 260415 Status 전환 시점은 반드시 기록 (Alive→Destroyed/Collided가 버킷 중복으로 누락되던 문제)
                bool hasPrevIdx = _lastLogIndex.TryGetValue(model.Id, out var lastIdxT);
                bool inOrBeforeLastT = hasPrevIdx && lastIdxT >= logIndex;
                int curStatus = (int)target.Status;
                bool statusChanged = !_lastTargetStatus.TryGetValue(model.Id, out var prev) || prev != curStatus;
                if (inOrBeforeLastT && !statusChanged) return;
                if (inOrBeforeLastT && statusChanged)
                {
                    // 같은(또는 이미 미래로 밀린) bucket 내 Status 전환 → 다음 sample로 미룸
                    logIndex = lastIdxT + 1;
                    gridTime = logIndex * OutputPeriod;
                }
                _lastLogIndex[model.Id] = logIndex;
                _lastTargetStatus[model.Id] = curStatus;
                WriteTargetRow(gridTime, target);
            }
            else if (model.Type == MtGun && model is Gun gun)
            {
                bool hasPrev = _lastLogIndex.TryGetValue(model.Id, out var lastIdx);
                bool inOrBeforeLast = hasPrev && lastIdx >= logIndex;
                bool fireChanged = !_lastGunFire.TryGetValue(model.Id, out var prevFire)
                                   || prevFire != gun.LastFireTargetId;
                if (inOrBeforeLast && !fireChanged) return;
                if (inOrBeforeLast && fireChanged)
                {
                    // 같은(또는 이미 미래로 밀린) bucket 내 Fire 전환 → 다음 sample로 미룸
                    logIndex = lastIdx + 1;
                    gridTime = logIndex * OutputPeriod;
                }
                _lastLogIndex[model.Id] = logIndex;
                _lastGunFire[model.Id] = gun.LastFireTargetId;
                WriteCiwsRow(gridTime, gun);
            }
        };
    }

    private void WriteTargetRow(double time, TargetBase target)
    {
        var llh = GeoUtil.EnuToLla(target.Pos, _engine.Origin);
        _targetCsvWriter!.WriteLine(string.Join(',', new[]
        {
            time.ToString("F4"),
            target.InputId.ToString(),
            target.Type.ToString(),
            ((int)target.Status).ToString(),
            llh.Lat.ToString("F8"),
            llh.Lon.ToString("F8"),
            llh.Hgt.ToString("F4"),
            "0",
            target.Pose.Pitch.ToString("F4"),
            target.Pose.Yaw.ToString("F4"),
        }));
    }

    private void WriteStatusRow(double time)
    {
        if (_statusCsvWriter is null || _c2 is null) return;

        string line = string.Join(',', new[]
        {
            time.ToString("F4"),
            _c2.TotalTargets.ToString(),
            _c2.DetectedCount.ToString(),
            _c2.InterceptSuccess.ToString(),
            _c2.InterceptFail.ToString(),
        });

        // 동일 Time에 여러 호출이 들어오면 마지막 값만 남기도록 버퍼링
        if (_pendingStatusLine is not null && time != _pendingStatusTime)
        {
            _statusCsvWriter.WriteLine(_pendingStatusLine);
            _statusCsvWriter.Flush();
        }
        _pendingStatusTime = time;
        _pendingStatusLine = line;
    }

    private void FlushPendingStatus()
    {
        if (_statusCsvWriter is null || _pendingStatusLine is null) return;
        _statusCsvWriter.WriteLine(_pendingStatusLine);
        _statusCsvWriter.Flush();
        _pendingStatusLine = null;
    }

    private void WriteCiwsRow(double time, Gun gun)
    {
        _ciwsCsvWriter!.WriteLine(string.Join(',', new[]
        {
            time.ToString("F4"),
            gun.InputId.ToString(),
            gun.Pose.Pitch.ToString("F4"),
            gun.Pose.Yaw.ToString("F4"),
            gun.LastFireTargetId.ToString(),
            gun.LastFireTargetType.ToString(),
            gun.TotalFired.ToString(),
        }));
    }

    private void RegisterCiws(List<RecordItem> ciwsItems)
    {
        int ciwsBaseId = 300;
        foreach (var ciws in ciwsItems)
        {
            var pos = new LLHPos(ciws.Position.Latitude, ciws.Position.Longitude, ciws.Position.Height);
            var (fcs, trackRadar, gun) = _engine.AddCIWS(ciwsBaseId, pos,
                TrackPeriod, FireRange,
                4500, 1000, 10, int.MaxValue, 60,               // Test Ammo 무한 (복원 시 1000 등 적정값)
                _c2!);
            fcs.InputId = ciws.Id;
            trackRadar.InputId = ciws.Id;
            gun.InputId = ciws.Id;
            ApplyTerrainToTrackRadar(trackRadar);
            ciwsBaseId += 10;
        }
    }

    private void RegisterTargets(List<RecordItem> targetItems)
    {
        // Test 직선 기동 예시용 공통 목표 지점 (월드 origin lat/lon, 고도 95m → ENU z=95)
        // 정식에서는 input의 Waypoint를 그대로 AddWaypoint로 연결하고 이 블록 제거
        var destination = new XYZPos(0.0, 0.0, 95.0);

        int tgtBaseId = 1;
        foreach (var tgt in targetItems)
        {
            var pos = new LLHPos(tgt.Position.Latitude, tgt.Position.Longitude, tgt.Position.Height);
            double azimuth = tgt.Rotation.Yaw;
            double elevation = tgt.Rotation.Pitch;

            var airplane = _engine.AddAirplane(tgtBaseId, pos, DefaultSpeed, azimuth, elevation, tgt.StartT,
                AirplaneSizeX, AirplaneSizeY, AirplaneSizeZ);
            airplane.InputId = tgt.Id;
            airplane.Destination = destination;   // Test (복원 시 제거)
            tgtBaseId++;
        }
    }

    private LLHPos CalcSiteCenter(List<RecordItem> ciwsItems)
    {
        if (ciwsItems.Count > 0)
            return CalcCenter(ciwsItems);
        return _engine.Origin;
    }

    private static LLHPos CalcCenter(List<RecordItem> items)
    {
        double latSum = 0, lonSum = 0, altSum = 0;
        foreach (var item in items)
        {
            latSum += item.Position.Latitude;
            lonSum += item.Position.Longitude;
            altSum += item.Position.Height;
        }
        return new LLHPos(latSum / items.Count, lonSum / items.Count, altSum / items.Count);
    }
}

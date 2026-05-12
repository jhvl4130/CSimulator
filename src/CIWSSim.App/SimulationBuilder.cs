using CIWSSimulator.Core;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using CIWSSimulator.Models;
using CIWSSimulator.Proto;
using CIWSSimulator.TerrainImport;
using Google.Protobuf;
using static CIWSSimulator.Core.SimConstants;
using ProtoEventType = CIWSSimulator.Proto.EventType;

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
    private const double BulletGridPeriod = 1.0;   // Bullet ObjectInfo 1Hz 그리드

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
    private LLHPos _siteCenterLlh;

    // 출력 스트림
    private FileStream? _initStream;
    private FileStream? _objectInfoStream;
    private FileStream? _eventStream;

    // UniqueId 카운터 (Target → CIWS → Radar → C2 → Bullet 동적, 모두 동일 풀)
    private uint _nextUniqueId = 1;

    // CIWS Gun 참조 (1Hz Bullet 그리드 enumerate용)
    private readonly List<Gun> _guns = new();

    // 1Hz Bullet 그리드 트리거 (다음 기록할 절대 시각)
    private double _nextBulletGridTime = BulletGridPeriod;

    // ObjectInfo 다운샘플링/Status 전환 보존 상태
    private readonly Dictionary<int, long> _lastLogIndex = new();
    private readonly Dictionary<int, int> _lastTargetStatus = new();
    private readonly Dictionary<int, int> _lastGunFire = new();

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

        _initStream?.Dispose();
        _objectInfoStream?.Dispose();
        _eventStream?.Dispose();
    }

    private void Build()
    {
        var ciwsItems = _input.Records
            .FirstOrDefault(r => r.Tag == "CombatPlatform.Ciws")?.Items ?? new();
        var targetItems = _input.Records
            .FirstOrDefault(r => r.Tag == "CombatPlatform.Target.Aircraft")?.Items ?? new();

        _engine.Origin = ResolveOrigin(ciwsItems);
        _siteCenterLlh = CalcSiteCenter(ciwsItems);

        _terrain = LoadTerrain();

        SetupProtobufOutput();

        // Engine 모델 등록 (의존성: C2 → CIWS → SearchRadar → AssetZone → Target)
        _c2 = _engine.AddC2Control(500);

        RegisterCiws(ciwsItems);

        var sr = _engine.AddSearchRadar(100, _siteCenterLlh, DetectRange, DetectPeriod);
        sr.C2 = _c2;
        ApplyTerrainToSearchRadar(sr);

        _engine.AddAssetZone(900, _siteCenterLlh, AssetRadius, _c2);

        RegisterTargets(targetItems);
        _c2.TotalTargets = targetItems.Count;

        // UniqueId 부여 + Init.pb 기록 (사용자 지정 순서: Target → CIWS → Radar → C2)
        AssignUniqueIdsAndWriteInit(sr);

        // Bullet UniqueId 콜백 + 이벤트 hook
        foreach (var gun in _guns)
            gun.AllocateBulletUniqueId = () => _nextUniqueId++;

        _engine.OnSimulationEvent = HandleSimEvent;
        _engine.OnModelTransitioned = HandleModelTransitioned;
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

        string metaPath = ResolveFileDir(_input.Terrain.MetaPath);

        if (!File.Exists(metaPath))
        {
            if (string.IsNullOrWhiteSpace(_input.Terrain.TiffPath))
                throw new FileNotFoundException(
                    $"지형 메타 파일이 없고 자동 빌드용 TiffPath도 비어있습니다: {metaPath}", metaPath);

            BuildTerrainFromTiff(metaPath);
        }

        var terrain = TerrainMap.LoadFromMeta(metaPath);

        const double OriginTolDeg = 1e-6;
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

    private void BuildTerrainFromTiff(string metaPath)
    {
        var cfg = _input.Terrain!;
        string tiffPath = ResolveFileDir(cfg.TiffPath);
        string outDir = Path.GetDirectoryName(metaPath) ?? FileDir;
        string name = Path.GetFileNameWithoutExtension(metaPath);

        Console.WriteLine($"[Terrain] 메타 없음 → 자동 빌드: tiff={tiffPath}, out={outDir}\\{name}.{{raw,json}}");

        TerrainBuilder.Build(new TerrainBuilder.Options
        {
            TiffPath = tiffPath,
            OriginLat = _engine.Origin.Lat,
            OriginLon = _engine.Origin.Lon,
            HalfSizeM = cfg.HalfSizeM,
            CellSizeM = cfg.CellSizeM,
            OutDir = outDir,
            Name = name,
        }, msg => Console.WriteLine($"[Terrain] {msg}"));
    }

    private string ResolveFileDir(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(FileDir, path);

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

    // 출력

    private void SetupProtobufOutput()
    {
        _initStream = new FileStream(Path.Combine(FileDir, "Init.pb"), FileMode.Create);
        _objectInfoStream = new FileStream(Path.Combine(FileDir, "ObjectInfo.pb"), FileMode.Create);
        _eventStream = new FileStream(Path.Combine(FileDir, "Event.pb"), FileMode.Create);
    }

    private void AssignUniqueIdsAndWriteInit(SearchRadar sr)
    {
        // 1) Targets
        foreach (var model in _engine.GetModelsByClass(ModelClass.Target))
        {
            if (model is not TargetBase target) continue;
            target.UniqueId = _nextUniqueId++;
            WriteInitForTarget(0.0, target);
        }

        // 2) CIWS Guns
        foreach (var gun in _guns)
        {
            gun.UniqueId = _nextUniqueId++;
            WriteInitForCiws(0.0, gun);
        }

        // 3) SearchRadar
        sr.UniqueId = _nextUniqueId++;
        WriteInitForRadar(0.0, sr);

        // 4) C2Control
        if (_c2 is not null)
        {
            _c2.UniqueId = _nextUniqueId++;
            WriteInitForC2(0.0, _c2);
        }
    }

    private static ObjectType ToObjectTypeForTarget(TargetBase target) => target switch
    {
        Airplane => ObjectType.Airplane,
        Drone => ObjectType.Drone,
        Uav => ObjectType.Uav,
        Rocket => ObjectType.Rocket,
        Missile => ObjectType.Missile1,
        _ => ObjectType.Na,
    };

    private void WriteInitForTarget(double t, TargetBase target)
    {
        var llh = GeoUtil.EnuToLla(target.Pos, _engine.Origin);
        WriteInit(new InitObject
        {
            Time = t,
            Id = target.UniqueId,
            Type = ToObjectTypeForTarget(target),
            Lat = llh.Lat,
            Lon = llh.Lon,
            Alt = llh.Hgt,
            Roll = 0.0,
            Pitch = target.Pose.Pitch,
            Yaw = target.Pose.Yaw,
            InitBullet = 0,
        });
    }

    private void WriteInitForCiws(double t, Gun gun)
    {
        var llh = GeoUtil.EnuToLla(gun.Pos, _engine.Origin);
        WriteInit(new InitObject
        {
            Time = t,
            Id = gun.UniqueId,
            Type = ObjectType.Ciws,
            Lat = llh.Lat,
            Lon = llh.Lon,
            Alt = llh.Hgt,
            Roll = 0.0,
            Pitch = gun.Pose.Pitch,
            Yaw = gun.Pose.Yaw,
            InitBullet = (uint)Math.Max(0, gun.Ammo),
        });
    }

    private void WriteInitForRadar(double t, SearchRadar sr)
    {
        var llh = GeoUtil.EnuToLla(sr.Pos, _engine.Origin);
        WriteInit(new InitObject
        {
            Time = t,
            Id = sr.UniqueId,
            Type = ObjectType.Radar,
            Lat = llh.Lat,
            Lon = llh.Lon,
            Alt = llh.Hgt,
            Roll = 0.0,
            Pitch = 0.0,
            Yaw = 0.0,
            InitBullet = 0,
        });
    }

    private void WriteInitForC2(double t, C2Control c2)
    {
        WriteInit(new InitObject
        {
            Time = t,
            Id = c2.UniqueId,
            Type = ObjectType.C2,
            Lat = _siteCenterLlh.Lat,
            Lon = _siteCenterLlh.Lon,
            Alt = _siteCenterLlh.Hgt,
            Roll = 0.0,
            Pitch = 0.0,
            Yaw = 0.0,
            InitBullet = 0,
        });
    }

    private void WriteInitForBullet(double t, uint uniqueId, XYZPos enuPos)
    {
        var llh = GeoUtil.EnuToLla(enuPos, _engine.Origin);
        WriteInit(new InitObject
        {
            Time = t,
            Id = uniqueId,
            Type = ObjectType.BulletA,
            Lat = llh.Lat,
            Lon = llh.Lon,
            Alt = llh.Hgt,
            Roll = 0.0,
            Pitch = 0.0,
            Yaw = 0.0,
            InitBullet = 0,
        });
    }

    private void WriteInit(InitObject msg)
    {
        if (_initStream is null) return;
        msg.WriteDelimitedTo(_initStream);
        _initStream.Flush();
    }

    private void WriteObjectInfo(ObjectInfo msg)
    {
        if (_objectInfoStream is null) return;
        msg.WriteDelimitedTo(_objectInfoStream);
        _objectInfoStream.Flush();
    }

    private void WriteEvent(EventEntry msg)
    {
        if (_eventStream is null) return;
        msg.WriteDelimitedTo(_eventStream);
        _eventStream.Flush();
    }

    // ObjectInfo 콜백 (Target/Gun 정기 + Bullet 1Hz 그리드)

    private void HandleModelTransitioned(double time, Model model)
    {
        // Bullet 1Hz 그리드 트리거 (모든 모델 전이 시점에 체크)
        FlushBulletGridUpTo(time);

        if (!model.IsStateChanged) return;
        model.IsStateChanged = false;

        long logIndex = (long)Math.Round(time / OutputPeriod);
        double gridTime = logIndex * OutputPeriod;

        if (model.Class == ModelClass.Target && model is TargetBase target)
        {
            // Status 전환 시점은 반드시 기록 (Alive→Destroyed/Collided가 버킷 중복으로 누락되던 문제)
            bool hasPrevIdx = _lastLogIndex.TryGetValue(model.Id, out var lastIdxT);
            bool inOrBeforeLastT = hasPrevIdx && lastIdxT >= logIndex;
            int curStatus = (int)target.Status;
            bool statusChanged = !_lastTargetStatus.TryGetValue(model.Id, out var prev) || prev != curStatus;
            if (inOrBeforeLastT && !statusChanged) return;
            if (inOrBeforeLastT && statusChanged)
            {
                logIndex = lastIdxT + 1;
                gridTime = logIndex * OutputPeriod;
            }
            _lastLogIndex[model.Id] = logIndex;
            _lastTargetStatus[model.Id] = curStatus;
            WriteTargetObjectInfo(gridTime, target);
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
                logIndex = lastIdx + 1;
                gridTime = logIndex * OutputPeriod;
            }
            _lastLogIndex[model.Id] = logIndex;
            _lastGunFire[model.Id] = gun.LastFireTargetId;
            WriteGunObjectInfo(gridTime, gun);
        }
    }

    private ObjectState GetTargetState(TargetBase target)
    {
        if (_c2 is null) return ObjectState.Basic;
        if (_c2.IsTracked(target.Id)) return ObjectState.Tracked;
        if (_c2.IsDetected(target.Id)) return ObjectState.Detected;
        return ObjectState.Basic;
    }

    private void WriteTargetObjectInfo(double time, TargetBase target)
    {
        var llh = GeoUtil.EnuToLla(target.Pos, _engine.Origin);
        WriteObjectInfo(new ObjectInfo
        {
            Time = time,
            Id = target.UniqueId,
            State = GetTargetState(target),
            Lat = llh.Lat,
            Lon = llh.Lon,
            Alt = llh.Hgt,
            Roll = 0.0,
            Pitch = target.Pose.Pitch,
            Yaw = target.Pose.Yaw,
        });
    }

    private void WriteGunObjectInfo(double time, Gun gun)
    {
        var llh = GeoUtil.EnuToLla(gun.Pos, _engine.Origin);
        WriteObjectInfo(new ObjectInfo
        {
            Time = time,
            Id = gun.UniqueId,
            State = ObjectState.Basic,
            Lat = llh.Lat,
            Lon = llh.Lon,
            Alt = llh.Hgt,
            Roll = 0.0,
            Pitch = gun.Pose.Pitch,
            Yaw = gun.Pose.Yaw,
        });
    }

    private void WriteBulletObjectInfo(double time, uint uniqueId, XYZPos enuPos)
    {
        var llh = GeoUtil.EnuToLla(enuPos, _engine.Origin);
        WriteObjectInfo(new ObjectInfo
        {
            Time = time,
            Id = uniqueId,
            State = ObjectState.Basic,
            Lat = llh.Lat,
            Lon = llh.Lon,
            Alt = llh.Hgt,
            Roll = 0.0,
            Pitch = 0.0,
            Yaw = 0.0,
        });
    }

    private void FlushBulletGridUpTo(double time)
    {
        while (time + 1e-9 >= _nextBulletGridTime)
        {
            double gridTime = _nextBulletGridTime;
            foreach (var gun in _guns)
            {
                foreach (var (uid, pos) in gun.ActiveBullets)
                {
                    WriteBulletObjectInfo(gridTime, uid, pos);
                }
            }
            _nextBulletGridTime += BulletGridPeriod;
        }
    }

    // 이벤트 콜백 (FIRE/HIT/SELF_DESTRUCT)

    private void HandleSimEvent(double t, uint uniqueId, SimEventKind kind, XYZPos enuPos)
    {
        if (kind == SimEventKind.Fire)
        {
            // 신규 Bullet → Init.pb 추가
            WriteInitForBullet(t, uniqueId, enuPos);
        }

        // Event.pb 기록
        WriteEvent(new EventEntry
        {
            Time = t,
            Id = uniqueId,
            Type = ToProtoEventType(kind),
        });

        // ObjectInfo.pb 추가 (이벤트 시점의 Bullet 위치)
        WriteBulletObjectInfo(t, uniqueId, enuPos);
    }

    private static ProtoEventType ToProtoEventType(SimEventKind kind) => kind switch
    {
        SimEventKind.Fire => ProtoEventType.Fire,
        SimEventKind.Hit => ProtoEventType.Hit,
        SimEventKind.SelfDestruct => ProtoEventType.SelfDestruct,
        _ => ProtoEventType.Fire,
    };

    // 모델 등록

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
            _guns.Add(gun);
            ciwsBaseId += 10;
        }
    }

    private void RegisterTargets(List<RecordItem> targetItems)
    {
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
            airplane.Destination = destination;
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

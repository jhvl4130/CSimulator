using System.Text;
using CIWSSimulator.Core;
using CIWSSimulator.Core.Geometry;
using CIWSSimulator.Core.Util;
using CIWSSimulator.Models;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.App;

public class SimulationBuilder
{
    // ── 상수 ──
    private const double SimEndTime = 60.0;   // Test 시뮬레이션 시간 단축 (원복 시 180.0)
    private const double DefaultSpeed = 200.0;
    private const double DetectRange = 10000.0;
    private const double DetectPeriod = 1.0;
    private const double TrackPeriod = 0.04;
    private const double FireRange = 10000.0;
    private const double SustainedFireKillSec = 3.0;   // Test 지속 사격 요격 임계시간 (복원 시 이 상수 및 사용처 제거)
    private const double AssetRadius = 500.0;

    /// <summary>
    /// 모든 입출력 파일이 위치하는 디렉토리
    /// </summary>
    private const string FileDir = @"D:\CIWSSimulator\File";

    private readonly InputConfig _input;
    private readonly Engine _engine = new();

    private C2Control? _c2;
    private StreamWriter? _targetCsvWriter;
    private StreamWriter? _ciwsCsvWriter;
    private readonly Dictionary<int, long> _lastLogIndex = new();
    // 260415 Target의 직전 Status 기록. Status 전환은 버킷 중복 체크를 우회해 반드시 기록
    private readonly Dictionary<int, int> _lastTargetStatus = new();

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

        _c2?.Dispose();
        _targetCsvWriter?.Dispose();
        _ciwsCsvWriter?.Dispose();
    }

    private void Build()
    {
        var ciwsItems = _input.Records
            .FirstOrDefault(r => r.Tag == "CombatPlatform.Ciws")?.Items ?? new();
        var targetItems = _input.Records
            .FirstOrDefault(r => r.Tag == "CombatPlatform.Target.Aircraft")?.Items ?? new();

        _engine.Origin = ResolveOrigin(ciwsItems);

        SetupCsvOutput();

        _c2 = _engine.AddC2Control(500, Path.Combine(FileDir, "event_log.csv"));

        RegisterCiws(ciwsItems);

        var siteCenter = CalcSiteCenter(ciwsItems);
        var sr = _engine.AddSearchRadar(100, siteCenter, DetectRange, DetectPeriod);
        sr.C2 = _c2;

        _engine.AddAssetZone(900, siteCenter, AssetRadius, _c2);

        RegisterTargets(targetItems);
    }

    private LLHPos ResolveOrigin(List<RecordItem> ciwsItems)
    {
        if (_input.WorldMapLLH is not null)
            return new LLHPos(_input.WorldMapLLH.Latitude, _input.WorldMapLLH.Longitude, 0.0);

        throw new InvalidOperationException("Origin을 결정할 수 없습니다.");
    }

    private void SetupCsvOutput()
    {
        _targetCsvWriter = new StreamWriter(Path.Combine(FileDir, "Target.csv"), false, Encoding.UTF8);
        _targetCsvWriter.WriteLine("Time,ID,Type,Status,Lat,Lon,Alt,Roll,Pitch,Yaw");

        _ciwsCsvWriter = new StreamWriter(Path.Combine(FileDir, "CIWS.csv"), false, Encoding.UTF8);
        // 260415 Fire 옆에 Tag 컬럼 추가 (사격 대상 Tag, 0이면 빈 문자열)
        _ciwsCsvWriter.WriteLine("Time,ID,Pitch,Yaw,Fire,Tag");

        _engine.OnModelTransitioned = (time, model) =>
        {
            if (!model.IsStateChanged) return;
            model.IsStateChanged = false;

            long logIndex = (long)Math.Round(time / OutputPeriod);
            bool sameBucket = _lastLogIndex.TryGetValue(model.Id, out var last) && last == logIndex;

            double gridTime = logIndex * OutputPeriod;

            if (model.Class == ModelClass.Target && model is TargetBase target)
            {
                // 260415 Status 전환 시점은 반드시 기록 (Alive→Destroyed/Collided가 버킷 중복으로 누락되던 문제)
                int curStatus = (int)target.Status;
                bool statusChanged = !_lastTargetStatus.TryGetValue(model.Id, out var prev) || prev != curStatus;
                if (sameBucket && !statusChanged) return;
                _lastLogIndex[model.Id] = logIndex;
                _lastTargetStatus[model.Id] = curStatus;
                WriteTargetRow(gridTime, target);
            }
            else if (model.Type == MtGun && model is Gun gun)
            {
                if (sameBucket) return;
                _lastLogIndex[model.Id] = logIndex;
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

    private void WriteCiwsRow(double time, Gun gun)
    {
        _ciwsCsvWriter!.WriteLine(string.Join(',', new[]
        {
            time.ToString("F4"),
            gun.InputId.ToString(),
            gun.Pose.Pitch.ToString("F4"),
            gun.Pose.Yaw.ToString("F4"),
            // 260415 0/1 → 0 또는 사격 대상 InputId
            // gun.LastFireFlag ? "1" : "0",
            gun.LastFireTargetId.ToString(),
            gun.LastFireTargetTag,
        }));
    }

    private void RegisterCiws(List<RecordItem> ciwsItems)
    {
        int ciwsBaseId = 300;
        foreach (var ciws in ciwsItems)
        {
            var pos = new LLHPos(ciws.Position.Latitude, ciws.Position.Longitude, ciws.Position.Height);
            var (fcs, trackRadar, gun) = _engine.AddCIWS(ciwsBaseId, pos,
                TrackPeriod, FireRange, SustainedFireKillSec,   // Test SustainedFireKillSec 인자 (복원 시 제거)
                4500, 1000, 10, int.MaxValue, 60,               // Test Ammo 무한 (복원 시 1000 등 적정값)
                _c2!);
            fcs.InputId = ciws.Id;
            trackRadar.InputId = ciws.Id;
            gun.InputId = ciws.Id;
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

            var airplane = _engine.AddAirplane(tgtBaseId, pos, DefaultSpeed, azimuth, elevation, tgt.StartT);
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

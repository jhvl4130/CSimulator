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
    private const double SimEndTime = 180.0;
    private const double DefaultSpeed = 200.0;
    private const double DetectRange = 10000.0;
    private const double DetectPeriod = 1.0;
    private const double TrackPeriod = 0.04;
    private const double FireRange = 1500.0;
    private const double AssetRadius = 2000.0;

    /// <summary>모든 입출력 파일이 위치하는 디렉토리.</summary>
    private const string FileDir = @"D:\CIWSSimulator\File";

    private readonly InputConfig _input;
    private readonly Engine _engine = new();

    private C2Control? _c2;
    private StreamWriter? _targetCsvWriter;
    private StreamWriter? _ciwsCsvWriter;
    private readonly Dictionary<int, long> _lastLogIndex = new();

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

        var areaRecord = _input.Records.FirstOrDefault(r => r.Tag == "Area");
        if (areaRecord is not null && areaRecord.Items.Count > 0)
        {
            var a = areaRecord.Items[0].Position;
            return new LLHPos(a.Latitude, a.Longitude, a.Height);
        }

        if (ciwsItems.Count > 0)
            return CalcCenter(ciwsItems);

        throw new InvalidOperationException("Origin을 결정할 수 없습니다.");
    }

    private void SetupCsvOutput()
    {
        _targetCsvWriter = new StreamWriter(Path.Combine(FileDir, "Target.csv"), false, Encoding.UTF8);
        _targetCsvWriter.WriteLine("Time,ID,Type,Status,Lat,Lon,Alt,Roll,Pitch,Yaw");

        _ciwsCsvWriter = new StreamWriter(Path.Combine(FileDir, "CIWS.csv"), false, Encoding.UTF8);
        _ciwsCsvWriter.WriteLine("Time,ID,Pitch,Yaw,Fire");

        _engine.OnModelTransitioned = (time, model) =>
        {
            if (!model.IsStateChanged) return;
            model.IsStateChanged = false;

            long logIndex = (long)Math.Round(time / OutputPeriod);
            if (_lastLogIndex.TryGetValue(model.Id, out var last) && last == logIndex) return;
            _lastLogIndex[model.Id] = logIndex;

            double gridTime = logIndex * OutputPeriod;

            if (model.Class == ModelClass.Target && model is TargetBase target)
            {
                WriteTargetRow(gridTime, target);
            }
            else if (model.Type == MtGun && model is Gun gun)
            {
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
            gun.LastFireFlag ? "1" : "0",
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
                4500, 1000, 10, 1000, 60,
                _c2!);
            fcs.InputId = ciws.Id;
            trackRadar.InputId = ciws.Id;
            gun.InputId = ciws.Id;
            ciwsBaseId += 10;
        }
    }

    private void RegisterTargets(List<RecordItem> targetItems)
    {
        int tgtBaseId = 1;
        foreach (var tgt in targetItems)
        {
            var pos = new LLHPos(tgt.Position.Latitude, tgt.Position.Longitude, tgt.Position.Height);
            double azimuth = tgt.Rotation.Yaw;
            double elevation = tgt.Rotation.Pitch;

            var airplane = _engine.AddAirplane(tgtBaseId, pos, DefaultSpeed, azimuth, elevation, 0.0);
            airplane.InputId = tgt.Id;
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

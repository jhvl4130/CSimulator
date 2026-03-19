using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using CIWSSim.Core.Util;

namespace CIWSSim.Core;

public class Engine
{
    /// <summary>등록된 전체 모델 (ID → Model)</summary>
    private readonly Dictionary<int, Model> _modelMap = new();

    /// <summary>시간 버킷별 스케줄 맵 (long 시간 → 클래스 우선순위별 모델 리스트)</summary>
    private readonly SortedDictionary<long, List<Model>[]> _schedMap = new();

    /// <summary>현재 버킷 처리 중 재스케줄링 대상 임시 버퍼</summary>
    private readonly List<Model> _schedTmp = new();

    /// <summary>현재 버킷에서 IntTrans 또는 ExtTrans가 호출된 모델 (상태 기록 대상)</summary>
    private readonly HashSet<Model> _transitioned = new();

    /// <summary>Platform 클래스 모델 목록 (충돌 판정 주체)</summary>
    private readonly List<Model> _platforms = new();

    /// <summary>충돌 판정 대상 모델 목록 (Weapon, Asset 등 Platform 외)</summary>
    private readonly List<Model> _collidables = new();
    
    /// <summary>시뮬레이션 종료 시 CSV로 출력할 상태 기록 누적 리스트</summary>
    private readonly List<StateRecord> _records = new();

    public double TL { get; private set; }

    /// <summary>ENU 원점의 LLH 좌표. ENU→LLH 변환 시 사용.</summary>
    public LLHPos Origin { get; set; }

    /// <summary>CSV 출력 경로. null이면 출력하지 않음.</summary>
    public string? OutputPath { get; set; } = "output.csv";

    public List<Model> GetCollidables() => _collidables;

    private void ScheduleModel(Model model, double time)
    {
        long t64 = (long)Math.Round(time * SimConstants.TScale);
        if (!_schedMap.TryGetValue(t64, out var bucket))
        {
            bucket = new List<Model>[SimConstants.ClsNum];
            for (int i = 0; i < SimConstants.ClsNum; i++)
                bucket[i] = new List<Model>();
            _schedMap[t64] = bucket;
        }
        bucket[(int)model.Class].Add(model);
    }

    private void RemoveModelFromSchedule(Model model)
    {
        foreach (var (_, bucket) in _schedMap)
        {
            foreach (var list in bucket)
            {
                list.Remove(model);
            }
        }
    }

    public void Start(double endT = SimConstants.TInfinite)
    {
        TL = 0.0;
        _records.Clear();

        Logger.Dbg("Simulation Start\n");

        foreach (var (_, model) in _modelMap)
        {
            double tN = model.Init(0.0);
            if (tN < SimConstants.TInfinite)
            {
                ScheduleModel(model, tN);
            }
        }

        while (_schedMap.Count > 0)
        {
            var first = _schedMap.First();
            long tKey = first.Key;
            var bucket = first.Value;

            TL = tKey / SimConstants.TScale;

            for (int i = 0; i < SimConstants.ClsNum; i++)
            {
                foreach (var model in bucket[i])
                {
                    double tA = model.IntTrans(TL);
                    _transitioned.Add(model);
                    if (tA > SimConstants.TContinue)
                    {
                        _schedTmp.Remove(model);
                        model.TA = TL + tA;
                        _schedTmp.Add(model);
                    }
                }
            }

            // 모든 IntTrans 완료 후 충돌 판정 일괄 수행
            CheckCollisions();

            // IntTrans 또는 ExtTrans가 발생한 모델만 기록
            RecordTransitioned();

            _schedMap.Remove(tKey);

            foreach (var model in _schedTmp)
            {
                RemoveModelFromSchedule(model);
                if (model.TA < SimConstants.TInfinite)
                {
                    ScheduleModel(model, model.TA);
                }
            }
            _schedTmp.Clear();

            if (TL >= endT)
            {
                Logger.Dbg($"Simulation end : time = {TL:F6}\n");
                break;
            }
        }

        if (_schedMap.Count == 0)
        {
            Logger.Dbg($"Simulation end : time = {TL:F6}\n");
        }

        ExportCsv();
    }

    private void RecordTransitioned()
    {
        foreach (var model in _transitioned)
        {
            if (model.IsEnabled)
            {
                _records.Add(new StateRecord(TL, model.Id, model.Type, model.Pos, model.Pose));
            }
        }
        _transitioned.Clear();
    }

    private void CheckCollisions()
    {
        foreach (var platform in _platforms)
        {
            if (!platform.IsEnabled) continue;

            foreach (var target in _collidables)
            {
                if (!target.IsEnabled) continue;
                if (CollisionDetection.IsCollide(platform.Pos, target.Building))
                {
                    Logger.Dbg(DbgFlag.Collide,
                        $"{TL:F6} [{platform.Name}] ↔ [{target.Name}] Collide\n");

                    var ev = new CollideEvent(platform.Power);
                    SendEvent(target, ev);

                    platform.IsEnabled = false;
                    platform.Phase = PhaseType.End;
                    _transitioned.Add(platform);
                    break;
                }
            }
        }
    }

    private void ExportCsv()
    {
        if (OutputPath is null || _records.Count == 0)
            return;

        var rows = _records.Select(r => r.ToCsvRow(Origin));
        FileIO.SaveCsv(OutputPath, StateRecord.CsvHeader, rows);
        Logger.Dbg($"CSV exported: {OutputPath} ({_records.Count} records)\n");
    }

    public void SendEvent(Model model, SimEvent ev)
    {
        double tA = model.ExtTrans(TL, ev);
        _transitioned.Add(model);
        if (tA > SimConstants.TContinue)
        {
            _schedTmp.Remove(model);
            model.TA = TL + tA;
            _schedTmp.Add(model);
        }
    }

    public void AddRuntimeModel(Model model)
    {
        model.Engine = this;
        _modelMap[model.Id] = model;
        if (model.Class == ModelClass.Platform)
            _platforms.Add(model);
        double tA = model.Init(TL);
        model.TA = TL + tA;
        _schedTmp.Add(model);
    }

    public void RegisterModel(Model model)
    {
        model.Engine = this;
        _modelMap[model.Id] = model;
    }

    public void RegisterPlatform(Model model)
    {
        RegisterModel(model);
        _platforms.Add(model);
    }

    public void RegisterCollidable(Model model)
    {
        RegisterModel(model);
        _collidables.Add(model);
    }

    public void AddWaypoint(int id, double x, double y, double z, double speed)
    {
        if (!_modelMap.TryGetValue(id, out var model))
        {
            Logger.Warn($"AddWaypoint: No target for ID '{id}'\n");
            return;
        }
        model.AddWaypoint(new XYZWayp(x, y, z, speed));
    }
}

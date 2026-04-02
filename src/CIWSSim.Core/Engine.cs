using System.Text;
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

    /// <summary>충돌 판정 대상 모델 목록 (Weapon, Asset 등)</summary>
    private readonly List<Model> _collidables = new();

    /// <summary>CSV 스트리밍 출력용 StreamWriter</summary>
    private StreamWriter? _csvWriter;

    /// <summary>현재 버킷에서 제거 요청된 모델 ID</summary>
    private readonly List<int> _removeQueue = new();

    public double TL { get; private set; }

    /// <summary>ENU 원점의 LLH 좌표. ENU→LLH 변환 시 사용.</summary>
    public LLHPos Origin { get; set; }

    /// <summary>CSV 출력 경로. null이면 출력하지 않음.</summary>
    public string? OutputPath { get; set; } = "output.csv";

    public List<Model> GetCollidables() => _collidables;

    /// <summary>ID로 모델 조회. 없으면 null.</summary>
    public Model? GetModel(int id) => _modelMap.GetValueOrDefault(id);

    /// <summary>지정 클래스에 해당하는 활성 모델 목록 반환.</summary>
    public IEnumerable<Model> GetModelsByClass(ModelClass cls)
    {
        foreach (var (_, model) in _modelMap)
        {
            if (model.Class == cls && model.IsEnabled)
                yield return model;
        }
    }

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

        // CSV 스트리밍 출력 초기화
        if (OutputPath is not null)
        {
            _csvWriter = new StreamWriter(OutputPath, false, Encoding.UTF8);
            _csvWriter.WriteLine(string.Join(',', StateRecord.CsvHeader));
        }

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

            // IntTrans 또는 ExtTrans가 발생한 모델만 기록 (스트리밍)
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

            // 제거 요청된 모델 처리
            ProcessRemoveQueue();

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

        // CSV 스트리밍 종료
        _csvWriter?.Dispose();
        _csvWriter = null;
    }

    private void RecordTransitioned()
    {
        if (_csvWriter is null)
        {
            _transitioned.Clear();
            return;
        }

        foreach (var model in _transitioned)
        {
            if (model.IsEnabled)
            {
                var record = new StateRecord(TL, model.Id, model.Type, model.Pos, model.Pose);
                _csvWriter.WriteLine(string.Join(',', record.ToCsvRow(Origin)));
            }
        }
        _transitioned.Clear();
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
        double tA = model.Init(TL);
        model.TA = TL + tA;
        _schedTmp.Add(model);
    }

    /// <summary>모델 제거 요청. 현재 버킷 처리 후 일괄 제거됨.</summary>
    public void RemoveModel(int id)
    {
        _removeQueue.Add(id);
    }

    private void ProcessRemoveQueue()
    {
        if (_removeQueue.Count == 0) return;

        foreach (var id in _removeQueue)
        {
            if (_modelMap.TryGetValue(id, out var model))
            {
                RemoveModelFromSchedule(model);
                _modelMap.Remove(id);
                _collidables.Remove(model);
            }
        }
        _removeQueue.Clear();
    }

    public void RegisterModel(Model model)
    {
        model.Engine = this;
        _modelMap[model.Id] = model;
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

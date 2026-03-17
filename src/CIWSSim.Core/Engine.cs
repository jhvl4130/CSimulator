using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;

namespace CIWSSim.Core;

public class Engine
{
    private readonly Dictionary<int, Model> _modelMap = new();
    private readonly SortedDictionary<long, List<Model>[]> _schedMap = new();
    private readonly List<Model> _schedTmp = new();
    private readonly List<Model> _assets = new();

    public double TL { get; private set; }

    public List<Model> GetAssets() => _assets;

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
        bucket[model.Class].Add(model);
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
                    if (tA > SimConstants.TContinue)
                    {
                        _schedTmp.Remove(model);
                        model.TA = TL + tA;
                        _schedTmp.Add(model);
                    }
                }
            }

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
    }

    public void SendEvent(Model model, SimEvent ev)
    {
        double tA = model.ExtTrans(TL, ev);
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

    public void RegisterModel(Model model)
    {
        model.Engine = this;
        _modelMap[model.Id] = model;
    }

    public void RegisterAsset(Model model)
    {
        RegisterModel(model);
        _assets.Add(model);
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

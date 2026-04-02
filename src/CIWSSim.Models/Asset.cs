using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Util;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

public class Asset : Model
{
    public Asset(int id) : base(id)
    {
        Class = ModelClass.Asset;
        Type = MtAsset;
        Name = $"Asset-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();
        return TInfinite;
    }

    public override double IntTrans(double t)
    {
        Logger.Err("Asset::IntTrans() - this function must not be called\n");
        return TInfinite;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        if (ev is CollideEvent col)
        {
            Health -= col.Power;
            if (Health < 0.0)
                Health = 0.0;

            Logger.Dbg(DbgFlag.Collide,
                $"{t:F6} [{Name}] Collide : health={Health:F6} power={col.Power:F6}\n");

            if (Health <= 0.0)
            {
                Logger.Dbg(DbgFlag.Collide, $"{t:F6} [{Name}] killed\n");
                IsEnabled = false;
            }
        }

        return TInfinite;
    }
}

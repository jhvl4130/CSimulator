using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Util;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

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
        switch (ev.Ev)
        {
            case SimEvent.EvCollide:
            {
                var col = (CollideEvent)ev;
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
                break;
            }
        }

        return TInfinite;
    }
}

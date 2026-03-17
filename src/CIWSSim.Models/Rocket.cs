using CIWSSim.Core;
using CIWSSim.Core.Events;
using CIWSSim.Core.Geometry;
using static CIWSSim.Core.SimConstants;

namespace CIWSSim.Models;

public class Rocket : Model
{
    public XYZPos Gip { get; set; }

    public Rocket(int id, XYZPos lp, XYZPos gip, double speed, double azi, double ele) : base(id)
    {
        Class = ModelClass.Platform;
        Type = MtRocket;
        Name = $"Rocket-{id}";

        IniPos = lp;
        Gip = gip;
        IniSpeed = speed;
        IniAzimuth = azi;
        IniElevation = ele;

        Power = 50.0;
    }

    public override double Init(double t)
    {
        InitRuntimeVars();

        Phase = PhaseRun;
        IsEnabled = true;
        Speed = IniSpeed;

        Logger.Dbg(DbgFlag.Init, $"{t:F6} [{Name}] created\n");

        return MovePeriod;
    }

    public override double IntTrans(double t)
    {
        double tN = TInfinite;

        switch (Phase)
        {
            case PhaseRun:
                tN = MovePeriod;
                // move (placeholder - same as C++ original)
                break;
        }

        return tN;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }
}

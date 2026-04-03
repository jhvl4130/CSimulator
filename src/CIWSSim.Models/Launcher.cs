using CIWSSimulator.Core;
using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;
using static CIWSSimulator.Core.SimConstants;

namespace CIWSSimulator.Models;

public class Launcher : Model
{
    // 설정 파라미터
    public int RktNum { get; set; } = 3;
    public double FirePeriod { get; set; } = 0.5;

    // 런타임 변수
    public int RemainRkt { get; set; }
    public int RktId { get; set; }

    public Launcher(int id) : base(id)
    {
        Class = ModelClass.Target;
        Type = MtLauncher;
        Name = $"Launcher-{id}";
    }

    public override double Init(double t)
    {
        InitRuntimeVars();

        RemainRkt = RktNum;
        RktId = Id * 10000;

        double tN = TInfinite;

        if (StartT > 0.0)
        {
            Phase = PhaseType.WaitStart;
            tN = StartT;
        }
        else
        {
            Phase = PhaseType.Run;
            if (RktNum > 0)
                tN = FirePeriod;
        }

        return tN;
    }

    public override double IntTrans(double t)
    {
        double tN = TInfinite;

        switch (Phase)
        {
            case PhaseType.WaitStart:
            {
                Phase = PhaseType.Run;
                if (RemainRkt > 0)
                {
                    FireRocket();
                    tN = RemainRkt <= 0 ? TInfinite : FirePeriod;
                }
                break;
            }
            case PhaseType.Run:
            {
                tN = FirePeriod;
                if (RemainRkt > 0)
                {
                    FireRocket();
                    if (RemainRkt <= 0)
                        tN = TInfinite;
                }
                break;
            }
        }

        return tN;
    }

    public override double ExtTrans(double t, SimEvent ev)
    {
        return TContinue;
    }

    private void FireRocket()
    {
        var rocket = new Rocket(RktId, IniPos, Speed, IniAzimuth, IniElevation);
        Engine!.AddRuntimeModel(rocket);
        RktId++;
        RemainRkt--;
    }
}

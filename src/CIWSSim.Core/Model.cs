using CIWSSimulator.Core.Events;
using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Core;

public abstract class Model
{
    public int Id { get; }
    public string Name { get; set; } = "";
    public ModelClass Class { get; set; }
    public int Type { get; set; }
    public string Tag { get; set; } = "";

    public Engine? Engine { get; set; }

    // 설정 값
    public double StartT { get; set; }
    public XYZPos IniPos { get; set; }
    public double IniSpeed { get; set; }
    public double IniAzimuth { get; set; }
    public double IniElevation { get; set; }

    public Building Building { get; set; } = new();

    public List<XYZWayp> Waypoints { get; } = new();

    public double Power { get; set; }

    // 바운딩 박스 반크기 (탄환-비행체 AABB 충돌 판정용)
    public double HalfX { get; set; }
    public double HalfY { get; set; }
    public double HalfZ { get; set; }

    // 런타임 값
    public PhaseType Phase { get; set; } = PhaseType.Run;
    public double TA { get; set; }

    // 현 위치 및 이동 정보
    public XYZPos Pos { get; set; }
    public Pose Pose { get; set; }
    public double Speed { get; set; }
    public bool IsStateChanged { get; set; } = true;

    // 상태
    public bool IsEnabled { get; set; } = true;
    public double Health { get; set; } = 100.0;

    protected Model(int id)
    {
        Id = id;
    }

    public abstract double Init(double t);
    public abstract double IntTrans(double t);
    public abstract double ExtTrans(double t, SimEvent ev);

    public void InitRuntimeVars()
    {
        Pos = IniPos;
        Pose = new Pose(IniAzimuth, IniElevation, 0.0);
        Health = 100.0;
    }

    public void AddWaypoint(XYZWayp wpt)
    {
        Waypoints.Add(wpt);
    }

    public void SetBuilding(Building building)
    {
        Building = building;
        Building.UpdateAABB();
    }
}

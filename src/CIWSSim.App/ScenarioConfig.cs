namespace CIWSSimulator.App;

public class ScenarioConfig
{
    public OriginDto Origin { get; set; } = new();
    public double SimEndTime { get; set; } = 100.0;
    public List<AirplaneDto> Airplanes { get; set; } = new();
    public List<LauncherDto> Launchers { get; set; } = new();
    public SearchRadarDto? SearchRadar { get; set; }
    public C2Dto? C2 { get; set; }
    public List<CiwsDto> Ciws { get; set; } = new();
    public List<AssetZoneDto> AssetZones { get; set; } = new();
}

public class OriginDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Alt { get; set; }
}

public class AirplaneDto
{
    public int Id { get; set; }
    public PositionDto Position { get; set; } = new();
    public SizeDto Size { get; set; } = new();
    public double Speed { get; set; }
    public double Azimuth { get; set; }
    public double Elevation { get; set; }
    public double StartT { get; set; }
    public List<WaypointDto> Waypoints { get; set; } = new();
}

public class PositionDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Alt { get; set; }
}

public class SizeDto
{
    public double LengthX { get; set; }
    public double WidthY { get; set; }
    public double HeightZ { get; set; }
}

public class WaypointDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double Alt { get; set; }
    public double Speed { get; set; }
}

public class LauncherDto
{
    public int Id { get; set; }
    public PositionDto Position { get; set; } = new();
    public double Speed { get; set; }
    public double Azimuth { get; set; }
    public double Elevation { get; set; }
    public double StartT { get; set; }
}

public class SearchRadarDto
{
    public int Id { get; set; }
    public PositionDto Position { get; set; } = new();
    public double DetectRange { get; set; } = 10000.0;
    public double DetectPeriod { get; set; } = 1.0;
}

public class C2Dto
{
    public int Id { get; set; }
}

public class CiwsDto
{
    public int Id { get; set; }
    public PositionDto Position { get; set; } = new();
    public TrackRadarDto TrackRadar { get; set; } = new();
    public FcsDto Fcs { get; set; } = new();
    public GunDto Gun { get; set; } = new();
}

public class TrackRadarDto
{
    public double TrackPeriod { get; set; } = 0.04;
}

public class FcsDto
{
    public double FireRange { get; set; } = 1500.0;
}

public class GunDto
{
    public double Rpm { get; set; } = 4500.0;
    public double BulletSpeed { get; set; } = 1000.0;
    public double BulletPower { get; set; } = 10.0;
    public int Ammo { get; set; } = 1000;
    public double SlewRate { get; set; } = 60.0;
}

public class AssetZoneDto
{
    public int Id { get; set; }
    public PositionDto Position { get; set; } = new();
    public double Radius { get; set; } = 200.0;
}

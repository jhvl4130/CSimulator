namespace CIWSSim.App;

public class ScenarioConfig
{
    public OriginDto Origin { get; set; } = new();
    public double SimEndTime { get; set; } = 100.0;
    public List<AirplaneDto> Airplanes { get; set; } = new();
    public List<BuildingDto> Buildings { get; set; } = new();
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

public class BuildingDto
{
    public int Id { get; set; }
    public PositionDto Sw { get; set; } = new();
    public PositionDto Ne { get; set; } = new();
    public double Bottom { get; set; }
    public double Top { get; set; }
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

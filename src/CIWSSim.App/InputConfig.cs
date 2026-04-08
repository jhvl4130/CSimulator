using System.Text.Json.Serialization;

namespace CIWSSimulator.App;

public class InputConfig
{
    public int Version { get; set; }
    public string GroupKey { get; set; } = "";
    public List<RecordGroup> Records { get; set; } = new();
}

public class RecordGroup
{
    public string Tag { get; set; } = "";
    public List<RecordItem> Items { get; set; } = new();
}

public class RecordItem
{
    public int Id { get; set; }
    public RecordPosition Position { get; set; } = new();
    public RecordRotation Rotation { get; set; } = new();
    public RecordCollision? Collision { get; set; }
}

public class RecordPosition
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Height { get; set; }
}

public class RecordRotation
{
    public double Pitch { get; set; }
    public double Yaw { get; set; }
    public double Roll { get; set; }
}

public class RecordCollision
{
    public RecordPosition CenterLLH { get; set; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

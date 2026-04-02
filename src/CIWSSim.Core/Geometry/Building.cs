namespace CIWSSimulator.Core.Geometry;

public class Building
{
    public List<XYPos> Polygon { get; set; } = new();
    public double Bottom { get; set; }
    public double Top { get; set; }

    // AABB cache
    public double MinX { get; private set; }
    public double MinY { get; private set; }
    public double MaxX { get; private set; }
    public double MaxY { get; private set; }

    public void UpdateAABB()
    {
        MinX = MinY = double.MaxValue;
        MaxX = MaxY = double.MinValue;

        foreach (var p in Polygon)
        {
            if (p.X < MinX) MinX = p.X;
            if (p.X > MaxX) MaxX = p.X;
            if (p.Y < MinY) MinY = p.Y;
            if (p.Y > MaxY) MaxY = p.Y;
        }
    }
}

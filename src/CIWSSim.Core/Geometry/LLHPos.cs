namespace CIWSSim.Core.Geometry;

public struct LLHPos
{
    public double Lat;
    public double Lon;
    public double Hgt;

    public LLHPos(double lat, double lon, double hgt)
    {
        Lat = lat;
        Lon = lon;
        Hgt = hgt;
    }
}

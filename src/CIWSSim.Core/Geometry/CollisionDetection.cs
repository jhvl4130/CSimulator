namespace CIWSSim.Core.Geometry;

public static class CollisionDetection
{
    public static bool IsPointInPolygon(double x, double y, List<XYPos> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (((polygon[i].Y > y) != (polygon[j].Y > y)) &&
                (x < (polygon[j].X - polygon[i].X) * (y - polygon[i].Y)
                      / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    public static bool IsCollide(in XYZPos pos, Building b)
    {
        // 1단계: Z축 범위 필터링
        if (pos.Z < b.Bottom || pos.Z > b.Top)
            return false;

        // 2단계: AABB 필터링
        if (pos.X < b.MinX || pos.X > b.MaxX || pos.Y < b.MinY || pos.Y > b.MaxY)
            return false;

        // 3단계: 정밀 다각형 판정 (Ray Casting)
        return IsPointInPolygon(pos.X, pos.Y, b.Polygon);
    }

    public static double Distance(in XYZPos a, in XYZPos b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}

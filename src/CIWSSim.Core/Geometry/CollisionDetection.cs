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

    /// <summary>
    /// 점이 반구(상반구) 내부에 있는지 판정.
    /// center의 Z를 바닥으로, 위쪽으로 radius만큼의 반구.
    /// </summary>
    public static bool IsInsideHemisphere(in XYZPos pos, in XYZPos center, double radius)
    {
        // 바닥 아래이면 false
        if (pos.Z < center.Z)
            return false;

        double dx = pos.X - center.X;
        double dy = pos.Y - center.Y;
        double dz = pos.Z - center.Z;
        return (dx * dx + dy * dy + dz * dz) <= (radius * radius);
    }
}

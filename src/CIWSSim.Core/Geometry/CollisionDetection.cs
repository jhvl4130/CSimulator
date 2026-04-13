namespace CIWSSimulator.Core.Geometry;

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
    /// 선분(p0→p1)과 AABB(center ± half)의 교차 판정 (Slab method)
    /// 탄환 궤적 선분과 비행체 바운딩 박스 충돌 판정용.
    /// </summary>
    public static bool IsSegmentAABB(in XYZPos p0, in XYZPos p1,
        in XYZPos center, double halfX, double halfY, double halfZ)
    {
        double dx = p1.X - p0.X;
        double dy = p1.Y - p0.Y;
        double dz = p1.Z - p0.Z;

        double minX = center.X - halfX, maxX = center.X + halfX;
        double minY = center.Y - halfY, maxY = center.Y + halfY;
        double minZ = center.Z - halfZ, maxZ = center.Z + halfZ;

        double tMin = 0.0, tMax = 1.0;

        // X slab
        if (Math.Abs(dx) < 1e-12)
        {
            if (p0.X < minX || p0.X > maxX) return false;
        }
        else
        {
            double inv = 1.0 / dx;
            double t1 = (minX - p0.X) * inv;
            double t2 = (maxX - p0.X) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }

        // Y slab
        if (Math.Abs(dy) < 1e-12)
        {
            if (p0.Y < minY || p0.Y > maxY) return false;
        }
        else
        {
            double inv = 1.0 / dy;
            double t1 = (minY - p0.Y) * inv;
            double t2 = (maxY - p0.Y) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }

        // Z slab
        if (Math.Abs(dz) < 1e-12)
        {
            if (p0.Z < minZ || p0.Z > maxZ) return false;
        }
        else
        {
            double inv = 1.0 / dz;
            double t1 = (minZ - p0.Z) * inv;
            double t2 = (maxZ - p0.Z) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) return false;
        }

        return true;
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

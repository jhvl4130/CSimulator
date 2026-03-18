using System;

namespace CIWSSim.Core.Geometry;

public static class GeoUtil
{
    // WGS-84 ellipsoid parameters
    private const double SemiMajor = 6378137.0;
    private const double Flattening = 1.0 / 298.257223563;
    private const double SemiMinor = SemiMajor * (1.0 - Flattening);
    private const double E2 = 1.0 - (SemiMinor * SemiMinor) / (SemiMajor * SemiMajor);

    // ── 단위 변환 ──

    public static double DegToRad(double deg) => deg * (Math.PI / 180.0);
    public static double RadToDeg(double rad) => rad * (180.0 / Math.PI);

    // ── 거리 / 방위각 ──

    /// <summary>
    /// 두 ENU 좌표 사이의 3D 유클리드 거리 (m).
    /// </summary>
    public static double Distance(in XYZPos a, in XYZPos b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double dz = b.Z - a.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// 두 ENU 좌표 사이의 2D 유클리드 거리 (m). Z 무시.
    /// </summary>
    public static double Distance2D(in XYZPos a, in XYZPos b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// a에서 b로의 진북 기준 방위각 (도). 0°=북, 90°=동, 시계 방향.
    /// ENU 좌표 기준 (X=동, Y=북).
    /// </summary>
    public static double Bearing(in XYZPos a, in XYZPos b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double rad = Math.Atan2(dx, dy);
        double deg = RadToDeg(rad);
        return (deg + 360.0) % 360.0;
    }

    // ── 위치 계산 ──

    /// <summary>
    /// 현재 위치에서 방위각/고각 방향으로 거리만큼 이동한 다음 위치를 반환.
    /// azimuth/elevation은 도 단위. ENU 좌표계 규칙 적용.
    /// </summary>
    public static XYZPos NextPosition(in XYZPos pos, double azimuth, double elevation, double distance)
    {
        double azRad = DegToRad(azimuth);
        double elRad = DegToRad(elevation);
        double cosEl = Math.Cos(elRad);

        return new XYZPos(
            pos.X + distance * cosEl * Math.Sin(azRad),
            pos.Y + distance * cosEl * Math.Cos(azRad),
            pos.Z + distance * Math.Sin(elRad));
    }

    /// <summary>
    /// XYPos 오버로드. 2D 방위각 방향으로 거리만큼 이동.
    /// </summary>
    public static XYPos NextPosition(in XYPos pos, double azimuth, double distance)
    {
        double azRad = DegToRad(azimuth);
        return new XYPos(
            pos.X + distance * Math.Sin(azRad),
            pos.Y + distance * Math.Cos(azRad));
    }

    /// <summary>
    /// XYZWayp 오버로드. 방위각/고각 방향으로 wayp.Speed * dt만큼 이동한 다음 웨이포인트를 반환.
    /// </summary>
    public static XYZWayp NextPosition(in XYZWayp wayp, double azimuth, double elevation, double dt)
    {
        double distance = wayp.Speed * dt;
        double azRad = DegToRad(azimuth);
        double elRad = DegToRad(elevation);
        double cosEl = Math.Cos(elRad);

        return new XYZWayp(
            wayp.X + distance * cosEl * Math.Sin(azRad),
            wayp.Y + distance * cosEl * Math.Cos(azRad),
            wayp.Z + distance * Math.Sin(elRad),
            wayp.Speed);
    }

    /// <summary>
    /// 현재 위치에서 목표 지점을 향한 방위각과 고각을 반환 (도 단위).
    /// </summary>
    public static Pose NextPose(in XYZPos from, in XYZPos to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;

        double azimuth = RadToDeg(Math.Atan2(dx, dy));
        azimuth = (azimuth + 360.0) % 360.0;

        double dist2D = Math.Sqrt(dx * dx + dy * dy);
        double elevation = RadToDeg(Math.Atan2(dz, dist2D));

        return new Pose(azimuth, elevation, 0.0);
    }

    /// <summary>
    /// XYPos 오버로드. 2D 방위각만 반환.
    /// </summary>
    public static Pose NextPose(in XYPos from, in XYPos to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;

        double azimuth = RadToDeg(Math.Atan2(dx, dy));
        azimuth = (azimuth + 360.0) % 360.0;

        return new Pose(azimuth, 0.0, 0.0);
    }

    /// <summary>
    /// XYZWayp 오버로드. 웨이포인트 간 방위각과 고각을 반환.
    /// </summary>
    public static Pose NextPose(in XYZWayp from, in XYZWayp to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;

        double azimuth = RadToDeg(Math.Atan2(dx, dy));
        azimuth = (azimuth + 360.0) % 360.0;

        double dist2D = Math.Sqrt(dx * dx + dy * dy);
        double elevation = RadToDeg(Math.Atan2(dz, dist2D));

        return new Pose(azimuth, elevation, 0.0);
    }

    // ── NED ↔ ENU 변환 ──

    /// <summary>
    /// NED → ENU 변환. (N,E,D) → (E,N,-D)
    /// </summary>
    public static XYZPos NedToEnu(in XYZPos ned)
    {
        return new XYZPos(ned.Y, ned.X, -ned.Z);
    }

    /// <summary>
    /// ENU → NED 변환. (E,N,U) → (N,E,-U)
    /// </summary>
    public static XYZPos EnuToNed(in XYZPos enu)
    {
        return new XYZPos(enu.Y, enu.X, -enu.Z);
    }

    // ── 좌표 변환 ──

    /// <summary>
    /// LLA(도 단위) → ENU 변환. origin은 ENU 원점의 LLA 좌표.
    /// </summary>
    public static XYZPos LlaToEnu(LLHPos pos, LLHPos origin)
    {
        var posEcef = LlaToEcef(pos);
        var originEcef = LlaToEcef(origin);

        double dx = posEcef.X - originEcef.X;
        double dy = posEcef.Y - originEcef.Y;
        double dz = posEcef.Z - originEcef.Z;

        double latRad = DegToRad(origin.Lat);
        double lonRad = DegToRad(origin.Lon);

        double sinLat = Math.Sin(latRad);
        double cosLat = Math.Cos(latRad);
        double sinLon = Math.Sin(lonRad);
        double cosLon = Math.Cos(lonRad);

        double e = -sinLon * dx + cosLon * dy;
        double n = -sinLat * cosLon * dx - sinLat * sinLon * dy + cosLat * dz;
        double u = cosLat * cosLon * dx + cosLat * sinLon * dy + sinLat * dz;

        return new XYZPos(e, n, u);
    }

    /// <summary>
    /// ENU → LLA(도 단위) 변환. origin은 ENU 원점의 LLA 좌표.
    /// </summary>
    public static LLHPos EnuToLla(XYZPos enu, LLHPos origin)
    {
        var originEcef = LlaToEcef(origin);

        double latRad = DegToRad(origin.Lat);
        double lonRad = DegToRad(origin.Lon);

        double sinLat = Math.Sin(latRad);
        double cosLat = Math.Cos(latRad);
        double sinLon = Math.Sin(lonRad);
        double cosLon = Math.Cos(lonRad);

        double dx = -sinLon * enu.X - sinLat * cosLon * enu.Y + cosLat * cosLon * enu.Z;
        double dy = cosLon * enu.X - sinLat * sinLon * enu.Y + cosLat * sinLon * enu.Z;
        double dz = cosLat * enu.Y + sinLat * enu.Z;

        return EcefToLla(new XYZPos(
            originEcef.X + dx,
            originEcef.Y + dy,
            originEcef.Z + dz));
    }

    // ── 내부 변환 (ECEF) ──

    private static XYZPos LlaToEcef(LLHPos lla)
    {
        double latRad = DegToRad(lla.Lat);
        double lonRad = DegToRad(lla.Lon);

        double sinLat = Math.Sin(latRad);
        double cosLat = Math.Cos(latRad);
        double sinLon = Math.Sin(lonRad);
        double cosLon = Math.Cos(lonRad);

        double n = SemiMajor / Math.Sqrt(1.0 - E2 * sinLat * sinLat);

        double x = (n + lla.Hgt) * cosLat * cosLon;
        double y = (n + lla.Hgt) * cosLat * sinLon;
        double z = (n * (1.0 - E2) + lla.Hgt) * sinLat;

        return new XYZPos(x, y, z);
    }

    private static LLHPos EcefToLla(XYZPos ecef)
    {
        double p = Math.Sqrt(ecef.X * ecef.X + ecef.Y * ecef.Y);
        double lon = Math.Atan2(ecef.Y, ecef.X);

        double lat = Math.Atan2(ecef.Z, p * (1.0 - E2));

        for (int i = 0; i < 10; i++)
        {
            double sinLat = Math.Sin(lat);
            double n = SemiMajor / Math.Sqrt(1.0 - E2 * sinLat * sinLat);
            lat = Math.Atan2(ecef.Z + E2 * n * sinLat, p);
        }

        double sinLatFinal = Math.Sin(lat);
        double nFinal = SemiMajor / Math.Sqrt(1.0 - E2 * sinLatFinal * sinLatFinal);
        double cosLatFinal = Math.Cos(lat);

        double hgt;
        if (Math.Abs(cosLatFinal) > 1e-10)
            hgt = p / cosLatFinal - nFinal;
        else
            hgt = Math.Abs(ecef.Z) / sinLatFinal - nFinal * (1.0 - E2);

        return new LLHPos(RadToDeg(lat), RadToDeg(lon), hgt);
    }
}

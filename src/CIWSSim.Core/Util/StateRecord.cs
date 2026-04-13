using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Core.Util;

/// <summary>
/// 모델의 한 시점 상태 기록. CSV 출력용.
/// </summary>
public readonly struct StateRecord
{
    public double Time { get; }
    public string Tag { get; }
    public int Id { get; }
    public XYZPos Pos { get; }
    public Pose Pose { get; }

    public StateRecord(double time, string tag, int id, XYZPos pos, Pose pose)
    {
        Time = time;
        Tag = tag;
        Id = id;
        Pos = pos;
        Pose = pose;
    }

    /// <summary>
    /// CSV 헤더
    /// </summary>
    public static readonly string[] CsvHeader =
        { "Time", "Tag", "ID", "Lat", "Lon", "Alt", "Roll", "Pitch", "Yaw" };

    /// <summary>
    /// ENU→LLH 변환 후 CSV 행으로 변환
    /// </summary>
    public string[] ToCsvRow(LLHPos origin)
    {
        var llh = GeoUtil.EnuToLla(Pos, origin);
        return new[]
        {
            Time.ToString("F4"),
            Tag,
            Id.ToString(),
            llh.Lat.ToString("F8"),
            llh.Lon.ToString("F8"),
            llh.Hgt.ToString("F4"),
            "0",                          // Roll
            Pose.Pitch.ToString("F4"),
            Pose.Yaw.ToString("F4")
        };
    }
}

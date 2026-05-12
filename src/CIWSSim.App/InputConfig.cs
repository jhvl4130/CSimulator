using System.Text.Json.Serialization;

namespace CIWSSimulator.App;

public class InputConfig
{
    public int Version { get; set; }
    public string GroupKey { get; set; } = "";
    public WorldMapLLH? WorldMapLLH { get; set; }
    public TerrainConfig? Terrain { get; set; }
    public List<RecordGroup> Records { get; set; } = new();
}

public class TerrainConfig
{
    /// <summary>지형 메타 JSON 경로 (FileDir 기준 상대 또는 절대)</summary>
    public string MetaPath { get; set; } = "";

    /// <summary>
    /// 메타 JSON이 없을 때 자동 빌드에 사용할 .tiff 경로 (FileDir 기준 상대 또는 절대).
    /// 비어있으면 자동 빌드 비활성 — MetaPath 파일이 없으면 throw.
    /// </summary>
    public string TiffPath { get; set; } = "";

    /// <summary>자동 빌드 시 ENU 원점 기준 ±반경(m). 기본 15000.</summary>
    public double HalfSizeM { get; set; } = 15000.0;

    /// <summary>자동 빌드 시 격자 셀 크기(m). 기본 30.</summary>
    public double CellSizeM { get; set; } = 30.0;

    /// <summary>LOS 광선 샘플 간격(m). 셀 크기 또는 그 절반 권장 (Nyquist).</summary>
    public double SampleStepM { get; set; } = 30.0;

    /// <summary>4/3 등가지구반경 보정 사용 여부</summary>
    public bool UseEarthCurvature { get; set; } = true;

    /// <summary>TrackRadar 연속 LOS 실패 시 TrackLost 임계 tick (hysteresis)</summary>
    public int LosLossTicks { get; set; } = 3;
}

public class WorldMapLLH
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
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
    public double StartT { get; set; }    // 260415 표적 시작 시각 (초)
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

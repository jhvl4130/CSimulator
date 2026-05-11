using System;
using System.IO;
using CIWSSimulator.Core.Util;

namespace CIWSSimulator.Core.Geometry;

/// <summary>
/// ENU 정렬 DEM 격자. 레이더 <-> 표적 간 가시선(LOS) 차폐 판정용.
/// (row=0, col=0)은 SW 코너, row 증가는 +Y(북), col 증가는 +X(동).
/// 해발 고도(m)는 float32로 저장된다.
/// </summary>
public sealed class TerrainMap
{
    /// <summary>해발 고도 격자 [row, col] (m)</summary>
    public float[,] Heights { get; }

    /// <summary>(row=0, col=0) 셀 중심의 ENU 좌표 (m)</summary>
    public XYPos OriginEnu { get; }

    /// <summary>격자 한 셀 변 길이 (m)</summary>
    public double CellSize { get; }

    public int Rows { get; }
    public int Cols { get; }

    /// <summary>격자를 생성할 때 사용한 LLH 기준 origin (시뮬 Engine.Origin 일치 검증용)</summary>
    public LLHPos OriginLlh { get; }

    public TerrainMap(float[,] heights, XYPos originEnu, double cellSize, LLHPos originLlh)
    {
        Heights = heights;
        OriginEnu = originEnu;
        CellSize = cellSize;
        OriginLlh = originLlh;
        Rows = heights.GetLength(0);
        Cols = heights.GetLength(1);
    }

    /// <summary>
    /// 메타 JSON과 동봉 .raw 파일을 읽어 TerrainMap을 만든다.
    /// raw 경로는 메타 JSON 디렉토리 기준 상대경로로 해석.
    /// </summary>
    public static TerrainMap LoadFromMeta(string metaJsonPath)
    {
        var meta = FileIO.LoadJson<TerrainMeta>(metaJsonPath)
            ?? throw new InvalidOperationException($"지형 메타 파일을 읽을 수 없습니다: {metaJsonPath}");
        if (meta.Rows <= 0 || meta.Cols <= 0)
            throw new InvalidOperationException($"지형 메타 rows/cols 값이 잘못되었습니다: {meta.Rows}x{meta.Cols}");
        if (meta.CellSizeM <= 0.0)
            throw new InvalidOperationException($"지형 메타 cellSizeM 값이 잘못되었습니다: {meta.CellSizeM}");
        if (string.IsNullOrWhiteSpace(meta.Raw))
            throw new InvalidOperationException("지형 메타에 raw 파일 경로가 없습니다");

        string baseDir = Path.GetDirectoryName(metaJsonPath) ?? ".";
        string rawPath = Path.IsPathRooted(meta.Raw) ? meta.Raw : Path.Combine(baseDir, meta.Raw);
        if (!File.Exists(rawPath))
            throw new FileNotFoundException($"지형 .raw 파일을 찾을 수 없습니다: {rawPath}");

        long expectedBytes = (long)meta.Rows * meta.Cols * 4L;
        long actualBytes = new FileInfo(rawPath).Length;
        if (actualBytes != expectedBytes)
            throw new InvalidOperationException(
                $"지형 .raw 파일 크기가 메타와 다릅니다: expected={expectedBytes}, actual={actualBytes}");

        var originEnu = new XYPos(meta.OriginEnu.X, meta.OriginEnu.Y);
        var originLlh = new LLHPos(meta.OriginLlh.Lat, meta.OriginLlh.Lon, 0.0);
        return Load(rawPath, meta.Rows, meta.Cols, originEnu, meta.CellSizeM, originLlh);
    }

    /// <summary>
    /// .raw 파일에서 격자 로드. 파일은 row-major float32 (rows * cols * 4 byte).
    /// 변환 도구·테스트가 메타 없이 직접 부를 때 사용.
    /// </summary>
    public static TerrainMap Load(string rawPath, int rows, int cols,
        XYPos originEnu, double cellSize, LLHPos originLlh = default)
    {
        var heights = new float[rows, cols];
        using var fs = File.OpenRead(rawPath);
        using var br = new BinaryReader(fs);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                heights[r, c] = br.ReadSingle();
        return new TerrainMap(heights, originEnu, cellSize, originLlh);
    }

    /// <summary>
    /// ENU (X, Y) 좌표에서 양선형 보간된 해발 고도(m).
    /// 격자 영역 밖이면 NegativeInfinity (= LOS를 절대 막지 않음).
    /// </summary>
    public double SampleHeight(double x, double y)
    {
        double colF = (x - OriginEnu.X) / CellSize;
        double rowF = (y - OriginEnu.Y) / CellSize;

        if (colF < 0 || rowF < 0 || colF > Cols - 1 || rowF > Rows - 1)
            return double.NegativeInfinity;

        int c0 = (int)Math.Floor(colF);
        int r0 = (int)Math.Floor(rowF);
        int c1 = Math.Min(c0 + 1, Cols - 1);
        int r1 = Math.Min(r0 + 1, Rows - 1);

        double fc = colF - c0;
        double fr = rowF - r0;

        double h00 = Heights[r0, c0];
        double h01 = Heights[r0, c1];
        double h10 = Heights[r1, c0];
        double h11 = Heights[r1, c1];

        double h0 = h00 * (1 - fc) + h01 * fc;
        double h1 = h10 * (1 - fc) + h11 * fc;
        return h0 * (1 - fr) + h1 * fr;
    }

    /// <summary>
    /// from → to ENU 직선이 지형에 의해 차폐되는지 판정.
    /// sampleStepM 간격으로 균등 샘플링하여 각 점의 지형고도와 광선고도를 비교.
    /// earthCurvature=true면 4/3 등가지구반경 보정으로 광선을 약간 위로 휘게 함.
    /// </summary>
    public bool HasLineOfSight(in XYZPos from, in XYZPos to,
        double sampleStepM, bool earthCurvature)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;
        double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (dist <= 0.0) return true;

        int nSamples = Math.Max(2, (int)Math.Ceiling(dist / sampleStepM));

        for (int k = 1; k < nSamples; k++)
        {
            double s = (double)k / nSamples;
            double x = from.X + dx * s;
            double y = from.Y + dy * s;
            double zRay = from.Z + dz * s;

            if (earthCurvature)
            {
                double d1 = dist * s;
                double d2 = dist - d1;
                zRay += d1 * d2 / (2.0 * EquivEarthRadius);
            }

            double zTerrain = SampleHeight(x, y);
            if (zTerrain >= zRay) return false;
        }
        return true;
    }

    // 4/3 등가지구반경 (대기 굴절 표준 모델)
    private const double EquivEarthRadius = 4.0 / 3.0 * 6378137.0;
}

/// <summary>
/// 지형 메타 JSON DTO. 변환 단계에서 .raw와 함께 생성된다.
/// </summary>
public class TerrainMeta
{
    public int Rows { get; set; }
    public int Cols { get; set; }
    public double CellSizeM { get; set; }
    public TerrainMetaXY OriginEnu { get; set; } = new();
    public TerrainMetaLlh OriginLlh { get; set; } = new();
    public string Raw { get; set; } = "";
}

public class TerrainMetaXY
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class TerrainMetaLlh
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using BitMiracle.LibTiff.Classic;
using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.TerrainImport;

/// <summary>
/// .tiff DEM (EPSG:4326, float32) → ENU 정렬 .raw + 메타 .json 변환기.
/// CLI(DemToRaw)와 App 양쪽에서 호출하는 라이브러리 진입점.
/// </summary>
public static class TerrainBuilder
{
    private const TiffTag ModelTiepointTag = (TiffTag)33922;   // 6 doubles: I,J,K, X,Y,Z
    private const TiffTag ModelPixelScaleTag = (TiffTag)33550; // 3 doubles: SX,SY,SZ
    private const TiffTag GdalNodataTag = (TiffTag)42113;      // ASCII

    public class Options
    {
        public string TiffPath { get; set; } = "";
        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        public double HalfSizeM { get; set; } = 15000.0;
        public double CellSizeM { get; set; } = 30.0;
        public string OutDir { get; set; } = "";
        public string Name { get; set; } = "terrain";
        public double? NodataOverride { get; set; }
    }

    public class BuildResult
    {
        public string RawPath { get; set; } = "";
        public string MetaPath { get; set; } = "";
        public int Rows { get; set; }
        public int Cols { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public int OutOfDem { get; set; }
        public int NodataHit { get; set; }
        public int Total { get; set; }
    }

    /// <summary>
    /// DEM .tiff에서 ENU 격자를 샘플링해 .raw + 메타 .json을 OutDir에 생성한다.
    /// </summary>
    public static BuildResult Build(Options opt, Action<string>? log = null)
    {
        log ??= _ => { };

        if (string.IsNullOrWhiteSpace(opt.TiffPath))
            throw new ArgumentException("TiffPath가 비어있습니다.", nameof(opt));
        if (string.IsNullOrWhiteSpace(opt.OutDir))
            throw new ArgumentException("OutDir이 비어있습니다.", nameof(opt));
        if (opt.HalfSizeM <= 0.0 || opt.CellSizeM <= 0.0)
            throw new ArgumentException("HalfSizeM/CellSizeM은 양수여야 합니다.", nameof(opt));
        if (!File.Exists(opt.TiffPath))
            throw new FileNotFoundException($"입력 .tiff 가 없습니다: {opt.TiffPath}", opt.TiffPath);

        int n = (int)Math.Round(2.0 * opt.HalfSizeM / opt.CellSizeM) + 1;
        int rows = n, cols = n;
        log($"[INFO] grid {rows}x{cols}, cell={opt.CellSizeM}m, half={opt.HalfSizeM}m");

        var origin = new LLHPos(opt.OriginLat, opt.OriginLon, 0.0);

        // 1. ENU 격자 각 셀의 (lon, lat) 계산
        var lons = new double[rows * cols];
        var lats = new double[rows * cols];
        double lonMin = double.PositiveInfinity, lonMax = double.NegativeInfinity;
        double latMin = double.PositiveInfinity, latMax = double.NegativeInfinity;
        for (int r = 0; r < rows; r++)
        {
            double y = -opt.HalfSizeM + r * opt.CellSizeM;
            for (int c = 0; c < cols; c++)
            {
                double x = -opt.HalfSizeM + c * opt.CellSizeM;
                var llh = GeoUtil.EnuToLla(new XYZPos(x, y, 0.0), origin);
                int idx = r * cols + c;
                lons[idx] = llh.Lon;
                lats[idx] = llh.Lat;
                if (llh.Lon < lonMin) lonMin = llh.Lon;
                if (llh.Lon > lonMax) lonMax = llh.Lon;
                if (llh.Lat < latMin) latMin = llh.Lat;
                if (llh.Lat > latMax) latMax = llh.Lat;
            }
        }
        log($"[INFO] ENU 격자 LLH 범위: lon [{lonMin:F6}, {lonMax:F6}], lat [{latMin:F6}, {latMax:F6}]");

        // 2. .tiff 메타 + sub-window 픽셀 범위 산출 후 해당 영역만 로드
        var heights = SampleTiff(opt.TiffPath, lons, lats, rows, cols, opt.NodataOverride,
            out var srcInfo, out var stats, log);

        // 3. 출력
        Directory.CreateDirectory(opt.OutDir);
        string rawName = $"{opt.Name}.raw";
        string rawPath = Path.Combine(opt.OutDir, rawName);
        string metaPath = Path.Combine(opt.OutDir, $"{opt.Name}.json");

        using (var fs = File.Create(rawPath))
        using (var bw = new BinaryWriter(fs))
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    bw.Write(heights[r * cols + c]);
        }

        var meta = new
        {
            rows,
            cols,
            cellSizeM = opt.CellSizeM,
            originEnu = new { x = -opt.HalfSizeM, y = -opt.HalfSizeM },
            originLlh = new { lat = opt.OriginLat, lon = opt.OriginLon },
            raw = rawName,
            source = new
            {
                crs = "EPSG:4326",
                path = Path.GetFullPath(opt.TiffPath),
                width = srcInfo.Width,
                height = srcInfo.Height,
                nodata = srcInfo.Nodata,
            },
        };
        File.WriteAllText(metaPath,
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        log($"[OK]  {rawPath}  ({new FileInfo(rawPath).Length} bytes)");
        log($"[OK]  {metaPath}");
        log($"[STATS] min={stats.Min:F1}  max={stats.Max:F1}  mean={stats.Mean:F1}  " +
            $"out-of-DEM={stats.OutOfDem}/{stats.Total}  nodata-hit={stats.NodataHit}/{stats.Total}");

        return new BuildResult
        {
            RawPath = rawPath,
            MetaPath = metaPath,
            Rows = rows,
            Cols = cols,
            Min = stats.Min,
            Max = stats.Max,
            Mean = stats.Mean,
            OutOfDem = stats.OutOfDem,
            NodataHit = stats.NodataHit,
            Total = stats.Total,
        };
    }

    // ─── TIFF 디코딩 ──────────────────────────────────────────────────

    private struct TiffInfo
    {
        public int Width, Height;
        public double TiepointI, TiepointJ, TiepointX, TiepointY;
        public double ScaleX, ScaleY;
        public double? Nodata;
    }

    private struct Stats
    {
        public double Min, Max, Mean;
        public int OutOfDem;   // ENU 격자가 DEM 영역 밖에 있어 0으로 채워진 셀 수
        public int NodataHit;  // 양선형 보간 4-corner 중 1개 이상이 nodata였던 셀 수
        public int Total;
    }

    private static float[] SampleTiff(string path, double[] lons, double[] lats,
        int rows, int cols, double? nodataOverride, out TiffInfo info, out Stats stats,
        Action<string> log)
    {
        using var tiff = Tiff.Open(path, "r");
        if (tiff is null)
            throw new InvalidOperationException($".tiff 열기 실패: {path}");

        if (tiff.IsTiled())
            throw new NotSupportedException("tile 기반 .tiff는 지원하지 않습니다. strip 기반으로 변환 후 다시 시도하세요.");

        int width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
        int height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        int bps = tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt();
        int spp = tiff.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
        var sfField = tiff.GetField(TiffTag.SAMPLEFORMAT);
        int sf = sfField is null ? (int)SampleFormat.UINT : sfField[0].ToInt();

        if (bps != 32 || sf != (int)SampleFormat.IEEEFP || spp != 1)
            throw new NotSupportedException(
                $"지원 형식 아님: bps={bps}, sampleFormat={sf}, samplesPerPixel={spp}. float32 단일 밴드만 지원.");

        var tp = ReadDoubleArrayField(tiff, ModelTiepointTag)
            ?? throw new InvalidOperationException("ModelTiepointTag(33922)가 없습니다 — GeoTIFF인지 확인하세요.");
        var ps = ReadDoubleArrayField(tiff, ModelPixelScaleTag)
            ?? throw new InvalidOperationException("ModelPixelScaleTag(33550)가 없습니다.");
        if (tp.Length < 6 || ps.Length < 3)
            throw new InvalidOperationException("Tiepoint/PixelScale 필드 길이가 부족합니다.");

        double tpI = tp[0], tpJ = tp[1], tpX = tp[3], tpY = tp[4];
        double sX = ps[0], sY = ps[1];

        double? nodata = nodataOverride ?? ParseNodata(tiff);

        info = new TiffInfo
        {
            Width = width, Height = height,
            TiepointI = tpI, TiepointJ = tpJ, TiepointX = tpX, TiepointY = tpY,
            ScaleX = sX, ScaleY = sY,
            Nodata = nodata,
        };

        // 1. ENU 격자의 LLH → DEM 픽셀 좌표 (col_f, row_f) 미리 계산
        int total = rows * cols;
        var colF = new double[total];
        var rowF = new double[total];
        double subColMin = double.PositiveInfinity, subColMax = double.NegativeInfinity;
        double subRowMin = double.PositiveInfinity, subRowMax = double.NegativeInfinity;
        for (int i = 0; i < total; i++)
        {
            // col = tpI + (lon - tpX) / sX
            // row = tpJ + (tpY - lat) / sY  (row 증가 = lat 감소; sY>0 가정)
            double cF = tpI + (lons[i] - tpX) / sX;
            double rF = tpJ + (tpY - lats[i]) / sY;
            colF[i] = cF;
            rowF[i] = rF;
            if (cF < subColMin) subColMin = cF;
            if (cF > subColMax) subColMax = cF;
            if (rF < subRowMin) subRowMin = rF;
            if (rF > subRowMax) subRowMax = rF;
        }

        int minCol = Math.Max(0, (int)Math.Floor(subColMin));
        int maxCol = Math.Min(width - 1, (int)Math.Ceiling(subColMax));
        int minRow = Math.Max(0, (int)Math.Floor(subRowMin));
        int maxRow = Math.Min(height - 1, (int)Math.Ceiling(subRowMax));
        if (minCol > maxCol || minRow > maxRow)
            throw new InvalidOperationException("ENU 격자가 DEM 영역 밖입니다.");

        int subW = maxCol - minCol + 1;
        int subH = maxRow - minRow + 1;
        log($"[INFO] DEM {width}x{height}, sub-window cols [{minCol}..{maxCol}] rows [{minRow}..{maxRow}] ({subW}x{subH})");

        // 2. sub-window 픽셀 로드
        var sub = LoadSubWindow(tiff, width, height, minCol, maxCol, minRow, maxRow);

        // 3. 양선형 보간 + 통계 누적
        var result = new float[total];
        double ndVal = nodata ?? double.NaN;
        double sum = 0.0;
        float minVal = float.PositiveInfinity;
        float maxVal = float.NegativeInfinity;
        int outOfDem = 0, ndHit = 0;
        for (int i = 0; i < total; i++)
        {
            double cF = colF[i] - minCol;
            double rF = rowF[i] - minRow;
            if (cF < 0 || rF < 0 || cF > subW - 1 || rF > subH - 1)
            {
                result[i] = 0.0f;
                outOfDem++;
                sum += 0.0;
                if (0.0f < minVal) minVal = 0.0f;
                if (0.0f > maxVal) maxVal = 0.0f;
                continue;
            }
            int c0 = (int)Math.Floor(cF);
            int r0 = (int)Math.Floor(rF);
            int c1 = Math.Min(c0 + 1, subW - 1);
            int r1 = Math.Min(r0 + 1, subH - 1);
            double fc = cF - c0;
            double fr = rF - r0;

            float v00 = sub[r0 * subW + c0];
            float v01 = sub[r0 * subW + c1];
            float v10 = sub[r1 * subW + c0];
            float v11 = sub[r1 * subW + c1];
            if (IsNodata(v00, ndVal) || IsNodata(v01, ndVal)
                || IsNodata(v10, ndVal) || IsNodata(v11, ndVal))
                ndHit++;

            double h00 = Filter(v00, ndVal);
            double h01 = Filter(v01, ndVal);
            double h10 = Filter(v10, ndVal);
            double h11 = Filter(v11, ndVal);
            double h0 = h00 * (1 - fc) + h01 * fc;
            double h1 = h10 * (1 - fc) + h11 * fc;
            float v = (float)(h0 * (1 - fr) + h1 * fr);
            result[i] = v;
            sum += v;
            if (v < minVal) minVal = v;
            if (v > maxVal) maxVal = v;
        }

        stats = new Stats
        {
            Min = minVal,
            Max = maxVal,
            Mean = sum / total,
            OutOfDem = outOfDem,
            NodataHit = ndHit,
            Total = total,
        };
        return result;
    }

    private static bool IsNodata(float v, double ndVal)
    {
        if (float.IsNaN(v)) return true;
        if (!double.IsNaN(ndVal) && Math.Abs(v - ndVal) < 1e-3) return true;
        return false;
    }

    private static double Filter(float v, double ndVal)
    {
        if (IsNodata(v, ndVal)) return 0.0;
        return v;
    }

    private static float[] LoadSubWindow(Tiff tiff, int width, int height,
        int minCol, int maxCol, int minRow, int maxRow)
    {
        int subW = maxCol - minCol + 1;
        int subH = maxRow - minRow + 1;
        var sub = new float[(long)subW * subH];

        int rowsPerStrip = tiff.GetField(TiffTag.ROWSPERSTRIP)[0].ToInt();
        int stripSize = tiff.StripSize();
        byte[] stripBuf = new byte[stripSize];

        int firstStrip = minRow / rowsPerStrip;
        int lastStrip = maxRow / rowsPerStrip;

        for (int strip = firstStrip; strip <= lastStrip; strip++)
        {
            int stripFirstRow = strip * rowsPerStrip;
            int rowsInStrip = Math.Min(rowsPerStrip, height - stripFirstRow);
            int bytesRead = tiff.ReadEncodedStrip(strip, stripBuf, 0, rowsInStrip * width * 4);
            if (bytesRead < 0)
                throw new InvalidOperationException($"strip {strip} 읽기 실패");

            int copyRowStart = Math.Max(minRow, stripFirstRow);
            int copyRowEnd = Math.Min(maxRow, stripFirstRow + rowsInStrip - 1);
            for (int r = copyRowStart; r <= copyRowEnd; r++)
            {
                int rowInStrip = r - stripFirstRow;
                int srcByteOffset = (rowInStrip * width + minCol) * 4;
                int dstOffset = (r - minRow) * subW;
                Buffer.BlockCopy(stripBuf, srcByteOffset, sub, dstOffset * 4, subW * 4);
            }
        }
        return sub;
    }

    private static double[]? ReadDoubleArrayField(Tiff tiff, TiffTag tag)
    {
        var f = tiff.GetField(tag);
        if (f is null) return null;
        // multi-value GeoTIFF 태그: f[0]=count, f[1]=byte[]
        if (f.Length < 2) return null;
        byte[] bytes = f[1].GetBytes();
        int n = bytes.Length / 8;
        var arr = new double[n];
        for (int i = 0; i < n; i++)
            arr[i] = BitConverter.ToDouble(bytes, i * 8);
        return arr;
    }

    private static double? ParseNodata(Tiff tiff)
    {
        var f = tiff.GetField(GdalNodataTag);
        if (f is null || f.Length < 2) return null;
        string s = f[1].ToString()?.Trim() ?? "";
        if (s.Length == 0) return null;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return v;
        return null;
    }
}

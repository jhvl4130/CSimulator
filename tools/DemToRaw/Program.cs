using System;
using System.IO;
using System.Text;
using System.Text.Json;
using BitMiracle.LibTiff.Classic;
using CIWSSimulator.Core.Geometry;

namespace CIWSSimulator.Tools.DemToRaw;

/// <summary>
/// .tiff DEM (EPSG:4326, float32) → ENU 정렬 .raw + 메타 .json 변환 도구.
///
/// 사용:
///   dotnet run --project tools/DemToRaw -- \
///       --tiff korea_dem.tif \
///       --origin-lat 37.545 --origin-lon 126.990265 \
///       --half-size 15000 --cell-size 30 \
///       --out-dir D:/CIWSSimulator/File --name terrain
/// </summary>
public static class Program
{
    private const TiffTag ModelTiepointTag = (TiffTag)33922;   // 6 doubles: I,J,K, X,Y,Z
    private const TiffTag ModelPixelScaleTag = (TiffTag)33550; // 3 doubles: SX,SY,SZ
    private const TiffTag GdalNodataTag = (TiffTag)42113;      // ASCII

    public static int Main(string[] args)
    {
        var opt = ParseArgs(args);
        if (opt is null) return 1;

        if (!File.Exists(opt.TiffPath))
        {
            Console.Error.WriteLine($"[ERR] 입력 .tiff 가 없습니다: {opt.TiffPath}");
            return 1;
        }

        int n = (int)Math.Round(2.0 * opt.HalfSize / opt.CellSize) + 1;
        int rows = n, cols = n;
        Console.WriteLine($"[INFO] grid {rows}x{cols}, cell={opt.CellSize}m, half={opt.HalfSize}m");

        var origin = new LLHPos(opt.OriginLat, opt.OriginLon, 0.0);

        // 1. ENU 격자 각 셀의 (lon, lat) 계산
        var lons = new double[rows * cols];
        var lats = new double[rows * cols];
        double lonMin = double.PositiveInfinity, lonMax = double.NegativeInfinity;
        double latMin = double.PositiveInfinity, latMax = double.NegativeInfinity;
        for (int r = 0; r < rows; r++)
        {
            double y = -opt.HalfSize + r * opt.CellSize;
            for (int c = 0; c < cols; c++)
            {
                double x = -opt.HalfSize + c * opt.CellSize;
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
        Console.WriteLine($"[INFO] ENU 격자 LLH 범위: lon [{lonMin:F6}, {lonMax:F6}], lat [{latMin:F6}, {latMax:F6}]");

        // 2. .tiff 메타 + sub-window 픽셀 범위 산출 후 해당 영역만 로드
        var heights = SampleTiff(opt.TiffPath, lons, lats, rows, cols, opt.NodataOverride,
            out var srcInfo, out var stats);

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
            cellSizeM = opt.CellSize,
            originEnu = new { x = -opt.HalfSize, y = -opt.HalfSize },
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

        Console.WriteLine($"[OK]  {rawPath}  ({new FileInfo(rawPath).Length} bytes)");
        Console.WriteLine($"[OK]  {metaPath}");
        Console.WriteLine(
            $"[STATS] min={stats.Min:F1}  max={stats.Max:F1}  mean={stats.Mean:F1}  " +
            $"out-of-DEM={stats.OutOfDem}/{stats.Total}  nodata-hit={stats.NodataHit}/{stats.Total}");
        return 0;
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
        int rows, int cols, double? nodataOverride, out TiffInfo info, out Stats stats)
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
        Console.WriteLine($"[INFO] DEM {width}x{height}, sub-window cols [{minCol}..{maxCol}] rows [{minRow}..{maxRow}] ({subW}x{subH})");

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
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double v))
            return v;
        return null;
    }

    // ─── CLI ──────────────────────────────────────────────────────────

    private class Options
    {
        public string TiffPath = "";
        public double OriginLat;
        public double OriginLon;
        public double HalfSize = 15000.0;
        public double CellSize = 30.0;
        public string OutDir = "";
        public string Name = "terrain";
        public double? NodataOverride;
    }

    private static Options? ParseArgs(string[] args)
    {
        var opt = new Options();
        bool hasLat = false, hasLon = false, hasTiff = false, hasOut = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--tiff": opt.TiffPath = args[++i]; hasTiff = true; break;
                case "--origin-lat": opt.OriginLat = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); hasLat = true; break;
                case "--origin-lon": opt.OriginLon = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); hasLon = true; break;
                case "--half-size": opt.HalfSize = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--cell-size": opt.CellSize = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--out-dir": opt.OutDir = args[++i]; hasOut = true; break;
                case "--name": opt.Name = args[++i]; break;
                case "--nodata": opt.NodataOverride = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "-h":
                case "--help":
                    PrintHelp(); return null;
                default:
                    Console.Error.WriteLine($"[ERR] 알 수 없는 인자: {a}");
                    PrintHelp(); return null;
            }
        }

        if (!hasTiff || !hasLat || !hasLon || !hasOut)
        {
            Console.Error.WriteLine("[ERR] --tiff, --origin-lat, --origin-lon, --out-dir 필수");
            PrintHelp();
            return null;
        }
        if (opt.HalfSize <= 0 || opt.CellSize <= 0)
        {
            Console.Error.WriteLine("[ERR] --half-size, --cell-size 는 양수여야 합니다");
            return null;
        }
        return opt;
    }

    private static void PrintHelp()
    {
        Console.Error.WriteLine(@"DemToRaw — .tiff DEM (EPSG:4326) → ENU 정렬 .raw + 메타 .json

옵션:
  --tiff <path>          입력 DEM .tiff (EPSG:4326, float32, strip 기반)
  --origin-lat <deg>     시뮬 ENU 원점 위도 (필수)
  --origin-lon <deg>     시뮬 ENU 원점 경도 (필수)
  --half-size <m>        ENU 원점 기준 ±반경 (기본 15000)
  --cell-size <m>        출력 격자 셀 크기 (기본 30)
  --out-dir <dir>        출력 디렉토리 (필수)
  --name <base>          출력 파일 베이스 이름 (기본 'terrain')
  --nodata <value>       GDAL_NODATA 태그 무시하고 강제 지정");
    }
}

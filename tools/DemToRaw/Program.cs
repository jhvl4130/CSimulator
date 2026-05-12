using System.Globalization;
using CIWSSimulator.TerrainImport;

namespace CIWSSimulator.Tools.DemToRaw;

/// <summary>
/// .tiff DEM (EPSG:4326, float32) → ENU 정렬 .raw + 메타 .json 변환 CLI.
/// 실제 변환 로직은 CIWSSim.TerrainImport.TerrainBuilder에 있다.
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
    public static int Main(string[] args)
    {
        var opt = ParseArgs(args);
        if (opt is null) return 1;

        try
        {
            TerrainBuilder.Build(opt, Console.WriteLine);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERR] {ex.Message}");
            return 1;
        }
    }

    private static TerrainBuilder.Options? ParseArgs(string[] args)
    {
        var opt = new TerrainBuilder.Options();
        bool hasLat = false, hasLon = false, hasTiff = false, hasOut = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--tiff": opt.TiffPath = args[++i]; hasTiff = true; break;
                case "--origin-lat": opt.OriginLat = double.Parse(args[++i], CultureInfo.InvariantCulture); hasLat = true; break;
                case "--origin-lon": opt.OriginLon = double.Parse(args[++i], CultureInfo.InvariantCulture); hasLon = true; break;
                case "--half-size": opt.HalfSizeM = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--cell-size": opt.CellSizeM = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--out-dir": opt.OutDir = args[++i]; hasOut = true; break;
                case "--name": opt.Name = args[++i]; break;
                case "--nodata": opt.NodataOverride = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
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
        if (opt.HalfSizeM <= 0 || opt.CellSizeM <= 0)
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

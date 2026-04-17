using System.Text;
using System.Text.Json;

namespace CIWSSimulator.Core.Util;

public static class FileIO
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // JSON

    public static T? LoadJson<T>(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }

    public static void SaveJson<T>(string path, T data)
    {
        var json = JsonSerializer.Serialize(data, JsonOpts);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    // CSV

    /// <summary>
    /// CSV 파일을 읽어 각 행을 문자열 배열로 반환한다.
    /// hasHeader가 true이면 첫 행을 건너뛴다.
    /// </summary>
    public static List<string[]> LoadCsv(string path, bool hasHeader = true)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var result = new List<string[]>();

        int start = hasHeader ? 1 : 0;
        for (int i = start; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            result.Add(line.Split(','));
        }

        return result;
    }

    /// <summary>
    /// 헤더와 행 데이터를 CSV 파일로 저장한다.
    /// </summary>
    public static void SaveCsv(string path, string[] header, IEnumerable<string[]> rows)
    {
        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(string.Join(',', header));
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',', row));
        }
    }

    /// <summary>
    /// 객체 리스트를 CSV로 저장한다. 프로퍼티를 자동으로 열로 매핑한다.
    /// </summary>
    public static void SaveCsv<T>(string path, IEnumerable<T> items)
    {
        var props = typeof(T).GetProperties();
        var header = props.Select(p => p.Name).ToArray();

        using var writer = new StreamWriter(path, false, Encoding.UTF8);
        writer.WriteLine(string.Join(',', header));
        foreach (var item in items)
        {
            var values = props.Select(p => Convert.ToString(p.GetValue(item)) ?? "");
            writer.WriteLine(string.Join(',', values));
        }
    }
}

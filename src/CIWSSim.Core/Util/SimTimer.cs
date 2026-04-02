using System.Diagnostics;

namespace CIWSSimulator.Core.Util;

/// <summary>
/// 시뮬레이션 실행 시간 측정 유틸리티.
/// </summary>
public class SimTimer : IDisposable
{
    private readonly Stopwatch _sw = new();
    private readonly string _label;

    public SimTimer(string label = "Elapsed")
    {
        _label = label;
        _sw.Start();
    }

    public double ElapsedMs => _sw.Elapsed.TotalMilliseconds;
    public double ElapsedSec => _sw.Elapsed.TotalSeconds;

    public void Stop()
    {
        _sw.Stop();
    }

    public void Restart()
    {
        _sw.Restart();
    }

    /// <summary>Dispose 시 경과 시간을 로그로 출력한다.</summary>
    public void Dispose()
    {
        _sw.Stop();
        Logger.Dbg($"[{_label}] {_sw.Elapsed.TotalMilliseconds:F2} ms\n");
    }
}

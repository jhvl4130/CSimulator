namespace CIWSSim.Core;

[Flags]
public enum DbgFlag : uint
{
    None = 0,
    Init = 0x00000001,
    Move = 0x00000002,
    Collide = 0x00000004,
}

public static class Logger
{
    public static DbgFlag DebugFlags { get; set; } = DbgFlag.Init | DbgFlag.Collide;

    public static void Dbg(string message)
    {
        Console.Write(message);
    }

    public static void Dbg(DbgFlag flag, string message)
    {
        if ((DebugFlags & flag) == 0)
            return;
        Console.Write(message);
    }

    public static void Warn(string message)
    {
        Console.Write($"WARN>{message}");
    }

    public static void Err(string message)
    {
        Console.Write($"ERR>{message}");
    }
}

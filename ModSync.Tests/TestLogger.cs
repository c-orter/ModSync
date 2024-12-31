using ModSync.Core.Util;

namespace ModSync.Tests;

public class TestLogger : ILogger
{
    public void LogDebug(object data)
    {
        Console.WriteLine($"DEBUG: {data}");
    }

    public void LogError(object data)
    {
        Console.WriteLine($"ERROR: {data}");
    }

    public void LogFatal(object data)
    {
        Console.WriteLine($"FATAL: {data}");
    }

    public void LogInfo(object data)
    {
        Console.WriteLine($"INFO: {data}");
    }

    public void LogMessage(object data)
    {
        Console.WriteLine($"MESSAGE: {data}");
    }

    public void LogWarning(object data)
    {
        Console.WriteLine($"WARNING: {data}");
    }
}

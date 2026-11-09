using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NJPlayer.Core.Diagnostics;

/// <summary>
/// Thread-safe file logger writing diagnostic logs and unhandled crash traces to %APPDATA%\NJPlayer\logs.
/// </summary>
public static class LogService
{
    private static readonly object SyncLock = new();
    private static readonly string LogsDirectory;

    public static string LogFolder => LogsDirectory;

    static LogService()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var flagPath = Path.Combine(baseDir, "portable.flag");
        var storage = File.Exists(flagPath)
            ? Path.Combine(baseDir, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NJPlayer");

        LogsDirectory = Path.Combine(storage, "logs");
        try
        {
            if (!Directory.Exists(LogsDirectory))
            {
                Directory.CreateDirectory(LogsDirectory);
            }
        }
        catch { }
    }

    public static void LogInfo(string message)
    {
        WriteLine("INFO", message);
    }

    public static void LogWarning(string message)
    {
        WriteLine("WARN", message);
    }

    public static void LogError(string context, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] [{context}] Thread: {Environment.CurrentManagedThreadId}");
        AppendExceptionDetails(sb, ex);

        var logText = sb.ToString();
        lock (SyncLock)
        {
            try
            {
                var errorLogPath = Path.Combine(LogsDirectory, "error.log");
                File.AppendAllText(errorLogPath, logText);
            }
            catch { }
        }

        Trace.WriteLine(logText);
    }

    public static string LogCrash(string context, Exception ex)
    {
        var crashFileName = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var crashFilePath = Path.Combine(LogsDirectory, crashFileName);

        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine($"NJ PLAYER UNHANDLED CRASH DUMP - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Context: {context}");
        sb.AppendLine($"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($".NET Runtime: {Environment.Version}");
        sb.AppendLine($"Process Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine("================================================================================");
        AppendExceptionDetails(sb, ex);

        var content = sb.ToString();
        lock (SyncLock)
        {
            try
            {
                File.WriteAllText(crashFilePath, content);
                var generalPath = Path.Combine(LogsDirectory, "error.log");
                File.AppendAllText(generalPath, content);
            }
            catch { }
        }

        Trace.WriteLine(content);
        return crashFilePath;
    }

    private static void AppendExceptionDetails(StringBuilder sb, Exception? ex, int depth = 0)
    {
        if (ex == null) return;
        var indent = new string(' ', depth * 2);
        sb.AppendLine($"{indent}Exception: {ex.GetType().FullName}: {ex.Message}");
        sb.AppendLine($"{indent}Source: {ex.Source}");
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            sb.AppendLine($"{indent}Stack Trace:\n{ex.StackTrace}");
        }

        if (ex is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
            {
                AppendExceptionDetails(sb, inner, depth + 1);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.AppendLine($"{indent}Inner Exception:");
            AppendExceptionDetails(sb, ex.InnerException, depth + 1);
        }
    }

    private static void WriteLine(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
        lock (SyncLock)
        {
            try
            {
                var generalPath = Path.Combine(LogsDirectory, "player.log");
                File.AppendAllText(generalPath, line);
            }
            catch { }
        }
        Trace.WriteLine(line);
    }
}

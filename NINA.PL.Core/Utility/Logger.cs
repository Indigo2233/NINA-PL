using System;
using Microsoft.Extensions.Logging;

namespace NINA.PL.Core;

/// <summary>
/// Static façade for application-wide logging via <see cref="Microsoft.Extensions.Logging"/>.
/// Call <see cref="Initialize"/> once at startup with an <see cref="ILoggerFactory"/>.
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static ILogger? _logger;

    /// <summary>
    /// Configures the core library logger category. Safe to call multiple times; last factory wins.
    /// </summary>
    /// <param name="factory">Non-null logger factory (typically from DI).</param>
    public static void Initialize(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate)
        {
            _logger = factory.CreateLogger("NINA.PL.Core");
        }
    }

    public static void Debug(string message)
    {
        Log(LogLevel.Debug, message);
    }

    public static void Debug(string message, params object?[] args)
    {
        Log(LogLevel.Debug, message, args);
    }

    public static void Info(string message)
    {
        Log(LogLevel.Information, message);
    }

    public static void Info(string message, params object?[] args)
    {
        Log(LogLevel.Information, message, args);
    }

    public static void Warn(string message)
    {
        Log(LogLevel.Warning, message);
    }

    public static void Warn(string message, params object?[] args)
    {
        Log(LogLevel.Warning, message, args);
    }

    public static void Warn(Exception exception, string message)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ILogger? log;
        lock (Gate)
        {
            log = _logger;
        }

        log?.LogWarning(exception, message);
    }

    public static void Warn(Exception exception, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ILogger? log;
        lock (Gate)
        {
            log = _logger;
        }

        log?.LogWarning(exception, message, args);
    }

    public static void Error(string message)
    {
        Log(LogLevel.Error, message);
    }

    public static void Error(string message, params object?[] args)
    {
        Log(LogLevel.Error, message, args);
    }

    public static void Error(Exception exception, string message)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ILogger? log;
        lock (Gate)
        {
            log = _logger;
        }

        log?.LogError(exception, message);
    }

    public static void Error(Exception exception, string message, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ILogger? log;
        lock (Gate)
        {
            log = _logger;
        }

        log?.LogError(exception, message, args);
    }

    private static void Log(LogLevel level, string message)
    {
        ILogger? log;
        lock (Gate)
        {
            log = _logger;
        }

        log?.Log(level, message);
    }

    private static void Log(LogLevel level, string message, object?[] args)
    {
        ILogger? log;
        lock (Gate)
        {
            log = _logger;
        }

        if (log is null)
        {
            return;
        }

        switch (level)
        {
            case LogLevel.Trace:
                log.LogTrace(message, args);
                break;
            case LogLevel.Debug:
                log.LogDebug(message, args);
                break;
            case LogLevel.Information:
                log.LogInformation(message, args);
                break;
            case LogLevel.Warning:
                log.LogWarning(message, args);
                break;
            case LogLevel.Error:
                log.LogError(message, args);
                break;
            case LogLevel.Critical:
                log.LogCritical(message, args);
                break;
            default:
                log.Log(level, message, args);
                break;
        }
    }
}

// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2022-2026 Aykut Alparslan KOC

using System;
using Serilog;
using Serilog.Core;

namespace Ferrite.Utils;

public class SerilogLogger : ILogger
{
    private Logger log;
    public SerilogLogger()
    {
        var path = Environment.GetEnvironmentVariable("FERRITE_LOG_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "ferrite.log";
        }
        // Zerogram: Information by default (Debug logs every request and floods disk/stdout under load).
        // FERRITE_LOG_LEVEL = Verbose|Debug|Information|Warning|Error|Fatal. Message text is never logged.
        _level = Enum.TryParse(Environment.GetEnvironmentVariable("FERRITE_LOG_LEVEL"), true,
            out Serilog.Events.LogEventLevel parsed) ? parsed : Serilog.Events.LogEventLevel.Information;
        log = new LoggerConfiguration()
            .MinimumLevel.Is(_level)
            .WriteTo.File(path, fileSizeLimitBytes: 10_000_000, rollOnFileSizeLimit: true,
                retainedFileCountLimit: 3, shared: false)
            .CreateLogger();
    }

    private readonly Serilog.Events.LogEventLevel _level;
    private bool Enabled(Serilog.Events.LogEventLevel level) => level >= _level;

    public void Debug(string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Debug)) return;
        Console.WriteLine(message);
        log.Debug(message);
    }

    public void Debug(Exception exception, string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Debug)) return;
        Console.WriteLine(message);
        log.Debug(exception, message);
    }

    public void Error(string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Error)) return;
        Console.WriteLine(message);
        log.Error(message);
    }

    public void Error(Exception exception, string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Error)) return;
        Console.WriteLine(message);
        log.Error(exception, message);
    }

    public void Fatal(string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Fatal)) return;
        Console.WriteLine(message);
        log.Fatal(message);
    }

    public void Fatal(Exception exception, string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Fatal)) return;
        Console.WriteLine(message);
        log.Fatal(exception, message);
    }

    public void Information(string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Information)) return;
        Console.WriteLine(message);
        log.Information(message);
    }

    public void Information(Exception exception, string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Information)) return;
        Console.WriteLine(message);
        log.Information(exception, message);
    }

    public void Verbose(string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Verbose)) return;
        Console.WriteLine(message);
        log.Verbose(message);
    }

    public void Verbose(Exception exception, string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Verbose)) return;
        Console.WriteLine(message);
        log.Verbose(exception, message);
    }

    public void Warning(string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Warning)) return;
        Console.WriteLine(message);
        log.Warning(message);
    }

    public void Warning(Exception exception, string message)
    {
        if (!Enabled(Serilog.Events.LogEventLevel.Warning)) return;
        Console.WriteLine(message);
        log.Warning(exception, message);
    }
}


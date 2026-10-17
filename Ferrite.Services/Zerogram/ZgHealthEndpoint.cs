// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: minimal local health endpoint (GET /healthz) for Docker / monitoring. It reports liveness and
// queue size only; it never exposes account names, message ids or content.

using System.Net;
using System.Text;
using System.Text.Json;
using Ferrite.Utils;

namespace Ferrite.Services.Zerogram;

public sealed class ZgHealthEndpoint : IDisposable
{
    private readonly ZgQueueStore _queue;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly DateTimeOffset _started;
    private HttpListener? _listener;
    private Task? _loop;

    public ZgHealthEndpoint(ZgQueueStore queue, TimeProvider time, ILogger log)
    {
        _queue = queue;
        _time = time;
        _log = log;
        _started = time.GetUtcNow();
    }

    public void Start(string bindAddress, int port)
    {
        if (port <= 0 || _listener is not null) return;
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://{bindAddress}:{port}/");
            _listener.Start();
            _loop = Task.Run(Loop);
            _log.Information($"zerogram: health endpoint on http://{bindAddress}:{port}/healthz");
        }
        catch (Exception e)
        {
            _log.Warning(e, "zerogram: health endpoint could not start");
            _listener = null;
        }
    }

    private async Task Loop()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { break; }
            try
            {
                bool ok = true;
                object body;
                try
                {
                    long now = _time.GetUtcNow().ToUnixTimeSeconds();
                    var stats = _queue.Stats(now);
                    body = new
                    {
                        status = "ok",
                        uptime_seconds = (long)(_time.GetUtcNow() - _started).TotalSeconds,
                        queue = new
                        {
                            messages = stats.Messages,
                            bytes = stats.Bytes,
                            recipients = stats.Recipients,
                            oldest_age_seconds = stats.OldestAgeSeconds,
                        },
                    };
                }
                catch (Exception e)
                {
                    ok = false;
                    body = new { status = "error", detail = e.GetType().Name };
                }
                bool path = context.Request.Url?.AbsolutePath == "/healthz";
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(path ? body : new { status = "not found" }));
                context.Response.StatusCode = !path ? 404 : ok ? 200 : 503;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }
            catch (Exception e)
            {
                _log.Debug(e, "zerogram: health request failed");
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }
    }

    public void Dispose()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
    }
}

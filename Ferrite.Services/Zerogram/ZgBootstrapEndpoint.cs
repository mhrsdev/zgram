// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: tiny HTTP endpoint (GET /zg/key) that lets the owner's app learn the server's public RSA key
// from just an IP address. The response is public information. Its authenticity is NOT taken from this
// channel: the owner sign-in proof (ZgOwnerService) is bound to the key fingerprint and to the owner
// password, so a man in the middle that swaps the key is detected before anything is trusted.

using System.Net;
using System.Text;
using System.Text.Json;
using Ferrite.Crypto;
using Ferrite.Utils;

namespace Ferrite.Services.Zerogram;

public sealed class ZgBootstrapEndpoint : IDisposable
{
    private readonly ZerogramOptions _options;
    private readonly IKeyProvider _keys;
    private IRSAKey _rsa => _keys.GetKey(_keys.GetRSAFingerprints()[0])!;
    private readonly ILogger _log;
    private HttpListener? _listener;

    public ZgBootstrapEndpoint(ZerogramOptions options, IKeyProvider keys, ILogger log)
    {
        _options = options;
        _keys = keys;
        _log = log;
    }

    public void Start()
    {
        if (_options.BootstrapPort <= 0 || _listener is not null) return;
        if ((_options.OwnerPassword?.Length ?? 0) < ZgOwnerService.MinPasswordLength) return; // owner sign-in is off
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://{(_options.BootstrapBind == "0.0.0.0" ? "+" : _options.BootstrapBind)}:{_options.BootstrapPort}/");
            _listener.Start();
            _ = Task.Run(Loop);
            _log.Information($"zerogram: key bootstrap endpoint on port {_options.BootstrapPort}");
        }
        catch (Exception e)
        {
            _log.Warning(e, "zerogram: key bootstrap endpoint could not start");
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
                bool ok = context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/zg/key";
                object body = ok
                    ? new
                    {
                        v = 1,
                        key = Convert.ToBase64String(_rsa.Key!.ExportRSAPublicKey()),
                        fp = unchecked((ulong)_rsa.Fingerprint).ToString("x16"),
                    }
                    : new { error = "not found" };
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
                context.Response.StatusCode = ok ? 200 : 404;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }
            catch (Exception e) { _log.Debug(e, "zerogram: bootstrap request failed"); }
            finally { try { context.Response.Close(); } catch { } }
        }
    }

    public void Dispose()
    {
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
    }
}

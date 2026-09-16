using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Ignixa.PackageManagement.Tests.Acquisition;

internal sealed class LoopbackHttpsRegistry : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly RSA _key = RSA.Create(2048);
    private readonly X509Certificate2 _certificate;
    private readonly Task _server;
    private readonly Process? _node;
    private readonly Task? _tlsErrors;
    private readonly bool _expectTrustRejection;
    private readonly ConcurrentQueue<string> _diagnostics = new();
    private bool _unexpectedStandardError;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Uri BaseUri { get; private set; }
    internal Task Ready => _ready.Task;
    internal IReadOnlyCollection<string> Diagnostics => _diagnostics.ToArray();
    internal List<(string Path, IReadOnlyDictionary<string, string> Headers)> Requests { get; } = [];
    internal Func<string, LoopbackReply> Respond { get; set; } = _ => new(404, [], false);

    internal LoopbackHttpsRegistry(bool expectTrustRejection = false)
    {
        _expectTrustRejection = expectTrustRejection;
        var request = new CertificateRequest("CN=localhost", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        _certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        _listener.Start();
        int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUri = new Uri($"https://127.0.0.1:{port}/");
        if (OperatingSystem.IsWindows())
        {
            // Schannel cannot serve ephemeral keys. The existing Node runtime accepts PEM through a pipe:
            // no private key files, persistent key containers, certificate stores or production bypass.
            _listener.Stop();
            _node = new Process
            {
                StartInfo = new ProcessStartInfo("node")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            _node.StartInfo.ArgumentList.Add("-e");
            _node.StartInfo.ArgumentList.Add(NodeServer);
            try
            {
                _node.Start();
            }
            catch
            {
                _node.Dispose();
                _certificate.Dispose();
                _key.Dispose();
                _stop.Dispose();
                _listener.Dispose();
                throw;
            }
            _node.StandardInput.AutoFlush = true;
            _tlsErrors = CaptureStandardErrorAsync();
            _server = ServeNodeAsync();
        }
        else
        {
            _ready.TrySetResult();
            _server = ServeAsync();
        }
    }

    internal bool IsExpectedCertificate(X509Certificate? certificate, SslPolicyErrors errors) =>
        certificate is not null &&
        (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 &&
        CryptographicOperations.FixedTimeEquals(certificate.GetRawCertData(), _certificate.RawData);

    private async Task ServeNodeAsync()
    {
        try
        {
            using var readinessTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            readinessTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            string config = JsonSerializer.Serialize(new
            {
                key = _key.ExportPkcs8PrivateKeyPem(),
                certificate = _certificate.ExportCertificatePem()
            });
            await _node!.StandardInput.WriteLineAsync(config.AsMemory(), readinessTimeout.Token);
            string ready = await _node.StandardOutput.ReadLineAsync(readinessTimeout.Token) ??
                throw new IOException("The test TLS process closed before listening.");
            using JsonDocument listening = JsonDocument.Parse(ready);
            if (listening.RootElement.TryGetProperty("processError", out JsonElement startupError))
            {
                throw new IOException($"The test TLS process failed before listening: {SafeCode(startupError.GetString())}.");
            }
            BaseUri = new Uri($"https://127.0.0.1:{listening.RootElement.GetProperty("port").GetInt32()}/");
            _ready.TrySetResult();
            while (!_stop.IsCancellationRequested)
            {
                string? line = await _node.StandardOutput.ReadLineAsync(_stop.Token);
                if (line is null)
                {
                    throw new IOException("The test TLS process closed unexpectedly.");
                }
                using JsonDocument message = JsonDocument.Parse(line);
                JsonElement root = message.RootElement;
                if (root.TryGetProperty("tlsError", out JsonElement tlsError))
                {
                    RecordHandshakeFailure(SafeCode(tlsError.GetString()));
                    continue;
                }
                if (root.TryGetProperty("processError", out JsonElement processError))
                {
                    throw new IOException($"The test TLS process failed: {SafeCode(processError.GetString())}.");
                }
                if (root.GetProperty("method").GetString() != "GET")
                {
                    throw new InvalidOperationException("Only GET is supported by the test registry.");
                }
                string path = root.GetProperty("path").GetString()!;
                var headers = root.GetProperty("headers").EnumerateObject()
                    .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.OrdinalIgnoreCase);
                Requests.Add((path, headers));
                LoopbackReply reply = Respond(path);
                var responseHeaders = reply.Headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                    .Select(header => header.Split(':', 2))
                    .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
                string response = JsonSerializer.Serialize(new
                {
                    id = root.GetProperty("id").GetInt32(),
                    status = reply.Status,
                    body = Convert.ToBase64String(reply.Body),
                    chunked = reply.Chunked,
                    headers = responseHeaders
                });
                await _node.StandardInput.WriteLineAsync(response.AsMemory(), _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (IOException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            throw;
        }
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                try
                {
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate,
                        EnabledSslProtocols = SslProtocols.None
                    }, _stop.Token);
                    _ = await RespondAsync(tls, tls, _stop.Token);
                }
                catch (AuthenticationException exception)
                {
                    RecordHandshakeFailure($"TLS_AUTHENTICATION_{exception.HResult:X8}");
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (SocketException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> RespondAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        var header = new StringBuilder();
        byte[] single = new byte[1];
        while (header.Length < 16384 && !header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            int read = await input.ReadAsync(single, cancellationToken);
            if (read == 0)
            {
                return false;
            }
            header.Append((char)single[0]);
        }

        string[] lines = header.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        string[] request = lines[0].Split(' ');
        if (request[0] != "GET")
        {
            throw new InvalidOperationException("Only GET is supported by the test registry.");
        }
        var headers = lines.Skip(1).Select(line => line.Split(':', 2))
            .ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        Requests.Add((request[1], headers));
        LoopbackReply reply = Respond(request[1]);
        string framing = reply.Chunked ? "Transfer-Encoding: chunked" : $"Content-Length: {reply.Body.Length}";
        byte[] prefix = Encoding.ASCII.GetBytes($"HTTP/1.1 {reply.Status} Test\r\n{framing}\r\nConnection: close\r\n{reply.Headers}\r\n");
        await output.WriteAsync(prefix, cancellationToken);
        if (reply.Chunked)
        {
            foreach (byte[] chunk in reply.Body.Chunk(13))
            {
                await output.WriteAsync(Encoding.ASCII.GetBytes($"{chunk.Length:X}\r\n"), cancellationToken);
                await output.WriteAsync(chunk, cancellationToken);
                await output.WriteAsync("\r\n"u8.ToArray(), cancellationToken);
            }
            await output.WriteAsync("0\r\n\r\n"u8.ToArray(), cancellationToken);
        }
        else
        {
            await output.WriteAsync(reply.Body, cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
        return true;
    }

    private void RecordHandshakeFailure(string code)
    {
        _diagnostics.Enqueue(code);
        if (!_expectTrustRejection)
        {
            throw new IOException($"Unexpected test TLS handshake failure: {code}.");
        }
    }

    private async Task CaptureStandardErrorAsync()
    {
        // Drain without retaining arbitrary process output (which can include configuration material).
        char[] buffer = new char[256];
        while (await _node!.StandardError.ReadAsync(buffer) != 0)
        {
            _unexpectedStandardError = true;
        }
    }

    private static string SafeCode(string? code) =>
        code is "ECONNRESET" or "ERR_SSL_SSLV3_ALERT_BAD_CERTIFICATE" or
            "ERR_SSL_TLSV1_ALERT_UNKNOWN_CA" or "ERR_SSL_TLSV1_ALERT_INTERNAL_ERROR" or
            "ERR_SSL_SSLV3_ALERT_CERTIFICATE_UNKNOWN" or "ERR_SSL_HTTP_REQUEST" or
            "ERR_SSL_WRONG_VERSION_NUMBER" or "EADDRINUSE" or "EPIPE"
            ? code : "TLS_UNCLASSIFIED_ERROR";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        if (_node is not null)
        {
            _node.StandardInput.Close();
            if (!_node.HasExited)
            {
                _node.Kill(entireProcessTree: true);
            }
            await _node.WaitForExitAsync();
        }
        try
        {
            await _server;
        }
        finally
        {
            _certificate.Dispose();
            _key.Dispose();
            _listener.Dispose();
            _stop.Dispose();
            if (_tlsErrors is not null)
            {
                await _tlsErrors;
            }
            _node?.Dispose();
        }
        if (_unexpectedStandardError)
        {
            throw new IOException("The test TLS process emitted unexpected stderr (TLS_PROCESS_STDERR).");
        }
    }

    private const string NodeServer = """
        const https = require('node:https');
        const readline = require('node:readline');
        const input = readline.createInterface({ input: process.stdin });
        const pending = new Map();
        let server;
        let nextId = 0;
        const code = error => typeof error.code === 'string' ? error.code : 'TLS_PROCESS_ERROR';
        process.on('uncaughtException', error => {
            console.log(JSON.stringify({ processError: code(error) }));
            process.exitCode = 1;
            server?.close();
            input.close();
        });
        input.on('line', line => {
            const value = JSON.parse(line);
            if (!server) {
                server = https.createServer({ key: value.key, cert: value.certificate, maxHeaderSize: 16384 }, (request, response) => {
                    const id = ++nextId;
                    pending.set(id, response);
                    console.log(JSON.stringify({ id, method: request.method, path: request.url, headers: request.headers }));
                });
                server.on('tlsClientError', error => console.log(JSON.stringify({ tlsError: code(error) })));
                server.listen(0, '127.0.0.1', () => console.log(JSON.stringify({ port: server.address().port })));
                return;
            }
            const response = pending.get(value.id);
            pending.delete(value.id);
            const bytes = Buffer.from(value.body, 'base64');
            const headers = { ...value.headers, Connection: 'close' };
            if (!value.chunked) headers['Content-Length'] = bytes.length;
            else headers['Transfer-Encoding'] = 'chunked';
            response.writeHead(value.status, headers);
            for (let offset = 0; offset < bytes.length; offset += 13) response.write(bytes.subarray(offset, offset + 13));
            response.end();
        });
        input.on('close', () => { server?.close(); process.exit(0); });
        """;
}

using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using TrafficLens.Models;

namespace TrafficLens.Core;

// Handles one TCP connection end-to-end: connects to the target, pumps bytes in both directions,
// fires TrafficCaptured for each chunk, and optionally rewrites the HTTP Host header.
internal sealed class ConnectionHandler : IDisposable
{
    private readonly TcpClient _clientSocket;
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly bool _rewriteHostHeader;

    private const int BufferSize = 65_536;

    // Set to false once the Host header has been rewritten so subsequent chunks
    // (body bytes of the same connection) are never touched.
    // Caveat: for HTTP keep-alive connections this means only the first request's Host
    // header is rewritten; subsequent requests on the same TCP connection are left as-is.
    // Full correctness would require parsing Content-Length / chunked encoding to detect
    // where each new request starts - out of scope for this tool.
    private bool _needsHostRewrite;

    // Matches "Host: <value>" in an HTTP header block (case-insensitive).
    private static readonly Regex HostHeaderRegex =
        new(@"(?i)^(Host:[ \t]*)([^\r\n]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    public event EventHandler<TrafficEventArgs>? TrafficCaptured;
    public event EventHandler<string>? ErrorOccurred;

    public ConnectionHandler(TcpClient clientSocket, string targetHost, int targetPort, bool rewriteHostHeader)
    {
        _clientSocket = clientSocket;
        _targetHost = NormalizeHost(targetHost);
        _targetPort = targetPort;
        _rewriteHostHeader = rewriteHostHeader;
        _needsHostRewrite = rewriteHostHeader;
    }

    // Strips URL brackets from IPv6 literals: [::1] -> ::1
    private static string NormalizeHost(string host) =>
        host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;

    // Connects to the target and starts forwarding in both directions.
    // Returns as soon as either side drops the connection.
    public async Task HandleAsync(CancellationToken ct)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var targetClient = new TcpClient();

        var clientEndpoint = _clientSocket.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var targetEndpoint = $"{_targetHost}:{_targetPort}";

        try
        {
            await targetClient.ConnectAsync(_targetHost, _targetPort, connectionCts.Token);

            var clientStream = _clientSocket.GetStream();
            var targetStream = targetClient.GetStream();

            var requestTask = ForwardAsync(clientStream, targetStream,
                                            TrafficDirection.Request,
                                            clientEndpoint, targetEndpoint,
                                            rewriteHost: _rewriteHostHeader,
                                            connectionCts.Token);

            var responseTask = ForwardAsync(targetStream, clientStream,
                                            TrafficDirection.Response,
                                            targetEndpoint, clientEndpoint,
                                            rewriteHost: false,
                                            connectionCts.Token);

            await Task.WhenAny(requestTask, responseTask);
            await connectionCts.CancelAsync();

            try { await requestTask; } catch (OperationCanceledException) { }
            try { await responseTask; } catch (OperationCanceledException) { }
        }
        catch (OperationCanceledException) { /* clean shutdown */ }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"[{clientEndpoint}] {ex.Message}");
        }
    }

    private async Task ForwardAsync(
        NetworkStream source,
        NetworkStream destination,
        TrafficDirection direction,
        string from,
        string to,
        bool rewriteHost,
        CancellationToken ct)
    {
        var buffer = new byte[BufferSize];

        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
            {
                var data = buffer[..bytesRead];

                if (rewriteHost && _needsHostRewrite)
                {
                    var rewritten = RewriteHostHeader(data, _targetHost);
                    if (!ReferenceEquals(rewritten, data))
                        _needsHostRewrite = false;
                    data = rewritten;
                }

                await destination.WriteAsync(data.AsMemory(), ct);
                RaiseTrafficCaptured(direction, from, to, data);
            }
        }
        catch (IOException) { /* remote end closed the connection */ }
        catch (OperationCanceledException) { throw; }
    }

    // Replaces the Host header value in the raw request bytes.
    // Latin-1 is used because it maps 0x00-0xFF losslessly (I mean: lossless byte<->char mapping for values 0x00-0xFF),
    // so binary bodies (e.g. file uploads) are never corrupted.
    private static byte[] RewriteHostHeader(byte[] data, string targetHost)
    {
        var text = Encoding.Latin1.GetString(data);
        var match = HostHeaderRegex.Match(text);

        if (!match.Success) return data;

        // IPv6 literals must be wrapped in brackets in the Host header (RFC 7230).
        // _targetHost is already normalized (no brackets), so re-add them if needed.
        var hostValue = targetHost.Contains(':') ? $"[{targetHost}]" : targetHost;

        var rewritten = text[..match.Index]
                        + match.Groups[1].Value  // "Host: "
                        + hostValue
                        + text[(match.Index + match.Length)..];

        return Encoding.Latin1.GetBytes(rewritten);
    }

    private void RaiseTrafficCaptured(TrafficDirection direction, string from, string to, byte[] data)
    {
        var entry = new TrafficEntry
        {
            Timestamp = DateTime.Now,
            Direction = direction,
            From = from,
            To = to,
            Data = [..data]
        };

        TrafficCaptured?.Invoke(this, new TrafficEventArgs { Entry = entry });
    }

    public void Dispose() => _clientSocket.Dispose();
}

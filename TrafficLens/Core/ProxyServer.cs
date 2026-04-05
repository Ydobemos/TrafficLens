using System.Net;
using System.Net.Sockets;
using System.Threading;
using TrafficLens.Models;

namespace TrafficLens.Core;

/// <summary>
/// Listens on a local TCP port and forwards all incoming connections to the configured target.
/// Raises events for each captured chunk so the UI can display traffic in real time.
/// </summary>
public sealed class ProxyServer : IAsyncDisposable
{
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly int _listenPort;
    private readonly bool _rewriteHostHeader;
    private readonly ListenMode _listenMode;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _activeConnections;

    // public events and properties

    public event EventHandler<TrafficEventArgs>? TrafficCaptured;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning { get; private set; }
    public int ActiveConnections => _activeConnections;

    public ProxyServer(string targetHost, int targetPort, int listenPort, bool rewriteHostHeader,
                       ListenMode listenMode = ListenMode.DualStack)
    {
        if (string.IsNullOrWhiteSpace(targetHost))
            throw new ArgumentException("Target host must not be empty.", nameof(targetHost));
        if (targetPort is < 1 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(targetPort), "Port must be in range 1-65535.");
        if (listenPort is < 1 or > 65_535)
            throw new ArgumentOutOfRangeException(nameof(listenPort), "Port must be in range 1-65535.");

        _targetHost = targetHost;
        _targetPort = targetPort;
        _listenPort = listenPort;
        _rewriteHostHeader = rewriteHostHeader;
        _listenMode = listenMode;
    }

    // start / stop

    /// <summary>
    /// Starts accepting connections. Blocks until <see cref="Stop"/> is called or the token is cancelled,
    /// so call this via <c>Task.Run()</c> to keep the UI thread free.
    /// </summary>
    public async Task StartAsync(CancellationToken externalToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("Proxy is already running.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

        _listener = _listenMode == ListenMode.IPv4Only
            ? new TcpListener(IPAddress.Any, _listenPort)
            : new TcpListener(IPAddress.IPv6Any, _listenPort);

        if (_listenMode == ListenMode.DualStack)
            _listener.Server.DualMode = true;

        _listener.Start();

        IsRunning = true;
        StatusChanged?.Invoke(this,
            $"Listening on port {_listenPort}  →  forwarding to {_targetHost}:{_targetPort}");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_cts.Token);
                // Fire-and-forget; errors surface through ErrorOccurred event.
                _ = HandleClientAsync(client, _cts.Token);
            }
        }
        catch (OperationCanceledException) { /* intentional stop */ }
        catch (SocketException ex) when (_cts.IsCancellationRequested
                                         || ex.SocketErrorCode == SocketError.Interrupted)
        {
            /* listener was stopped - expected */
        }
        finally
        {
            _listener.Stop();
            IsRunning = false;
            StatusChanged?.Invoke(this, "Stopped");
        }
    }

    public void Stop() => _cts?.Cancel();

    // per-connection handling

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        Interlocked.Increment(ref _activeConnections);

        using var handler = new ConnectionHandler(client, _targetHost, _targetPort, _rewriteHostHeader);
        handler.TrafficCaptured += (_, e) => TrafficCaptured?.Invoke(this, e);
        handler.ErrorOccurred += (_, msg) => ErrorOccurred?.Invoke(this, msg);

        try
        {
            await handler.HandleAsync(ct);
        }
        finally
        {
            Interlocked.Decrement(ref _activeConnections);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null) return;

        await _cts.CancelAsync();
        _cts.Dispose();
        _cts = null;
    }
}

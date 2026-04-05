using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrafficLens.Core;
using TrafficLens.Models;

namespace TrafficLens.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    // settings

    [ObservableProperty] private string _targetHost = "localhost";
    [ObservableProperty] private decimal? _targetPort = 5000;
    [ObservableProperty] private decimal? _listenPort = 8080;
    [ObservableProperty] private bool _rewriteHostHeader = false;

    // 0 = IPv4 only, 1 = dual-stack, 2 = IPv6 only
    [ObservableProperty] private int _listenModeIndex = 1;

    public static string[] ListenModeOptions { get; } = ["IPv4 only", "IPv4 + IPv6", "IPv6 only"];

    // runtime state

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRunning), nameof(StatusBrush))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveConnectionsText))]
    private int _activeConnections;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCapturedText))]
    private int _totalCaptured;

    [ObservableProperty] private string _statusText = "Stopped";

    public bool IsNotRunning => !IsRunning;
    public string ActiveConnectionsText => $"Active: {ActiveConnections}";
    public string TotalCapturedText => $"Captured: {TotalCaptured}";

    public IBrush StatusBrush => IsRunning
        ? new SolidColorBrush(Color.FromRgb(40, 167, 69))
        : new SolidColorBrush(Color.FromRgb(220, 53, 69));

    // traffic log

    public ObservableCollection<TrafficEntryViewModel> Requests { get; } = [];
    public ObservableCollection<TrafficEntryViewModel> Responses { get; } = [];

    private ProxyServer? _proxyServer;
    private CancellationTokenSource? _runCts;

    public MainWindowViewModel()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (_proxyServer is not null)
                ActiveConnections = _proxyServer.ActiveConnections;
        };
        timer.Start();
    }

    // commands

    [RelayCommand]
    private async Task StartAsync()
    {
        var host = TargetHost.Trim();
        var targetPort = (int)(TargetPort ?? 5000);
        var listenPort = (int)(ListenPort ?? 8080);

        if (string.IsNullOrWhiteSpace(host))
        {
            StatusText = "Error: Target host is empty.";
            return;
        }

        var normalizedHost = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        bool isLocalhost = normalizedHost is "localhost" or "127.0.0.1" or "::1";
        if (isLocalhost && targetPort == listenPort)
        {
            StatusText = "Error: Target port and listen port must differ for localhost.";
            return;
        }

        var listenMode = ListenModeIndex switch
        {
            0 => ListenMode.IPv4Only,
            2 => ListenMode.IPv6Only,
            _ => ListenMode.DualStack,
        };

        _proxyServer = new ProxyServer(host, targetPort, listenPort, RewriteHostHeader, listenMode);
        _proxyServer.TrafficCaptured += OnTrafficCaptured;
        _proxyServer.StatusChanged += OnStatusChanged;
        _proxyServer.ErrorOccurred += OnErrorOccurred;

        _runCts = new CancellationTokenSource();
        IsRunning = true;

        try
        {
            // Run the blocking accept-loop on the thread pool so the UI stays responsive.
            await Task.Run(() => _proxyServer.StartAsync(_runCts.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            await _proxyServer.DisposeAsync();
            _proxyServer = null;
            _runCts?.Dispose();
            _runCts = null;
            IsRunning = false;
            ActiveConnections = 0;
            StatusText = "Stopped";
        }
    }

    [RelayCommand]
    private void Stop() => _runCts?.Cancel();

    [RelayCommand]
    private void Clear()
    {
        Requests.Clear();
        Responses.Clear();
        TotalCaptured = 0;
    }

    // event handlers

    private void OnTrafficCaptured(object? sender, TrafficEventArgs e)
    {
        // TrafficEntryViewModel creates SolidColorBrush instances, which must be
        // constructed on the UI thread, so the whole object is created inside the Post.
        Dispatcher.UIThread.Post(() =>
        {
            var vm = new TrafficEntryViewModel(e.Entry);

            if (e.Entry.Direction == TrafficDirection.Request)
                Requests.Add(vm);
            else
                Responses.Add(vm);

            TotalCaptured++;
        });
    }

    private void OnStatusChanged(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => StatusText = message);
    }

    private void OnErrorOccurred(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => Requests.Add(TrafficEntryViewModel.ForError(message)));
    }
}

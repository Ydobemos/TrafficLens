using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using TrafficLens.ViewModels;

namespace TrafficLens.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _currentVm;

    public MainWindow()
    {
        InitializeComponent();
    }

    // auto-scroll: keep the latest entry visible when new items arrive

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Unsubscribe from the previous ViewModel's collections.
        if (_currentVm is not null)
        {
            _currentVm.Requests.CollectionChanged -= OnRequestsChanged;
            _currentVm.Responses.CollectionChanged -= OnResponsesChanged;
        }

        _currentVm = DataContext as MainWindowViewModel;

        if (_currentVm is not null)
        {
            _currentVm.Requests.CollectionChanged += OnRequestsChanged;
            _currentVm.Responses.CollectionChanged += OnResponsesChanged;
        }
    }

    private void OnRequestsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.UIThread.Post(() => RequestsScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void OnResponsesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.UIThread.Post(() => ResponsesScroll.ScrollToEnd(), DispatcherPriority.Background);
    }
}

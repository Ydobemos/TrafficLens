using Avalonia.Media;
using TrafficLens.Models;

namespace TrafficLens.ViewModels;

/// <summary>
/// Display model for one captured traffic chunk. Holds its own brush instances so the
/// DataTemplate doesn't need value converters.
/// Note: must be created on the UI thread because the Avalonia brush constructors require it.
/// </summary>
public sealed class TrafficEntryViewModel
{
    public string Label { get; }
    public string Endpoints { get; }
    public string TimestampAndBytes { get; }
    public string Body { get; }
    public IBrush HeaderBrush { get; }
    public IBrush TimestampBrush { get; }

    public TrafficEntryViewModel(TrafficEntry entry)
    {
        bool isRequest = entry.Direction == TrafficDirection.Request;

        Label = isRequest ? "REQUEST" : "RESPONSE";
        Endpoints = $"{entry.From} → {entry.To}";
        TimestampAndBytes = $"[{entry.Timestamp:HH:mm:ss.fff}]  {entry.ByteCount} bytes";
        Body = entry.FormattedData;

        HeaderBrush = new SolidColorBrush(isRequest
            ? Color.FromRgb(30, 100, 200)
            : Color.FromRgb(20, 150, 80));

        TimestampBrush = new SolidColorBrush(isRequest
            ? Color.FromRgb(192, 223, 255)  // light blue on blue header
            : Color.FromRgb(192, 255, 212)); // light green on green header
    }

    /// <summary>Creates a special error entry to show connection problems in the Requests panel.</summary>
    public static TrafficEntryViewModel ForError(string message) => new(message);

    private TrafficEntryViewModel(string errorMessage)
    {
        Label = "ERROR";
        Endpoints = DateTime.Now.ToString("HH:mm:ss.fff");
        TimestampAndBytes = string.Empty;
        Body = errorMessage;
        HeaderBrush = new SolidColorBrush(Color.FromRgb(200, 30, 50));
        TimestampBrush = Brushes.White;
    }
}

namespace TrafficLens.Models;

/// <summary>Carries a single captured <see cref="TrafficEntry"/> through the event pipeline.</summary>
public sealed class TrafficEventArgs : EventArgs
{
    public required TrafficEntry Entry { get; init; }
}

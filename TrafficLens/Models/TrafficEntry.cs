using System.Text;

namespace TrafficLens.Models;

/// <summary>Direction of a captured traffic chunk relative to the proxy.</summary> 
public enum TrafficDirection
{
    /// <summary>Data travelling from the connecting client towards the target server.</summary>
    Request,
    /// <summary>Data travelling from the target server back to the connecting client.</summary>
    Response
}

/// <summary>
/// Immutable snapshot of a single captured TCP data chunk.
/// </summary>
public sealed record TrafficEntry
{
    public DateTime Timestamp { get; init; }
    public TrafficDirection Direction { get; init; }
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public byte[] Data { get; init; } = [];
    public int ByteCount => Data.Length;

    /// <summary>
    /// Returns the payload as a readable string. Text content is decoded as UTF-8;
    /// binary data is rendered as a hex dump.
    /// </summary>
    public string FormattedData => IsTextual(Data)
        ? Encoding.UTF8.GetString(Data)
        : FormatHexDump(Data);

    // Treat as text if >= 80% of the first 512 bytes are printable ASCII / whitespace.
    private static bool IsTextual(byte[] data)
    {
        if (data.Length == 0) return true;

        int sample = Math.Min(data.Length, 512);
        int textual = 0;

        for (int i = 0; i < sample; i++)
        {
            byte b = data[i];
            if (b >= 0x20 || b is 0x09 or 0x0A or 0x0D)
                textual++;
        }

        return textual >= sample * 0.8;
    }

    private static string FormatHexDump(byte[] data)
    {
        const int bytesPerLine = 16;
        var sb = new StringBuilder(data.Length * 4);

        for (int offset = 0; offset < data.Length; offset += bytesPerLine)
        {
            sb.Append($"{offset:X8}  ");

            int lineEnd = Math.Min(offset + bytesPerLine, data.Length);

            for (int j = offset; j < lineEnd; j++)
                sb.Append($"{data[j]:X2} ");

            // Padding for incomplete last line
            for (int j = lineEnd; j < offset + bytesPerLine; j++)
                sb.Append("   ");

            sb.Append("  ");

            for (int j = offset; j < lineEnd; j++)
            {
                byte b = data[j];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

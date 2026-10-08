using System.Diagnostics;
using System.Globalization;

namespace ManagedBlf.Viewer;

public sealed record PreviewRow(ulong Number, string Type, string Time, string Channel,
    string Identifier, string Direction, string Dlc, string Data, string Flags, string Text,
    string Warning, string RawHex);

public sealed record PreviewResult(IReadOnlyList<PreviewRow> Rows, BlfFileHeader Header,
    ulong ObjectCount, ulong DecodeWarnings, TimeSpan Elapsed, bool PreviewLimited);

/// <summary>Scans the whole file while retaining only a bounded preview, independently of the UI.</summary>
public static class PreviewLoader
{
    public const int PreviewLimit = 10_000;

    public static PreviewResult Load(Stream input, CancellationToken cancellation,
        IProgress<(long Position, long End, ulong Count)>? progress = null)
    {
        using var reader = new BlfReader(input, leaveOpen: true);
        var rows = new List<PreviewRow>();
        var timer = Stopwatch.StartNew();
        long lastReport = 0;
        ulong count = 0, warnings = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!reader.ReadNext(out var value)) break;
            count++;
            BlfMessage? decoded = null;
            string warning = "";
            try
            {
                BlfMessageDecoder.TryDecode(value!, out decoded);
                warning = decoded?.Warning ?? "";
            }
            catch (InvalidDataException error)
            {
                warning = error.Message;
            }
            if (warning.Length != 0) warnings++;
            if (rows.Count < PreviewLimit)
            {
                string flags = decoded is null ? "Raw" : string.Join(" ", new[]
                {
                    decoded.Protocol.StartsWith("CAN", StringComparison.Ordinal) ? decoded.IsExtended ? "EXT" : "STD" : "",
                    decoded.IsRemote ? "RTR" : "",
                    decoded.BitrateSwitch ? "BRS" : "", decoded.ErrorStateIndicator ? "ESI" : ""
                }.Where(x => x.Length > 0));
                // Retain capped strings, never entire multi-megabyte unknown objects.
                rows.Add(new PreviewRow(count, BlfObjectTypes.GetName(value!.Header.ObjectType),
                    value.TimestampNanoseconds is { } ns ? (ns / 1_000_000_000m).ToString("F9", CultureInfo.InvariantCulture) : "—",
                    decoded?.Channel?.ToString(CultureInfo.InvariantCulture) ?? "—",
                    decoded?.Identifier is { } id ? $"0x{id:X}" : "—",
                    decoded is null || decoded.Protocol == "Text" ? "—" : decoded.IsTransmit ? "Tx" : "Rx",
                    decoded?.Dlc?.ToString(CultureInfo.InvariantCulture) ?? "—",
                    Hex(decoded?.Data ?? value.Body, 64), flags, Limit(decoded?.Text ?? "", 512),
                    warning, Hex(value.RawData, 256)));
            }
            if (timer.ElapsedMilliseconds - lastReport >= 100)
            {
                progress?.Report((reader.FilePosition, reader.DataEnd, count));
                lastReport = timer.ElapsedMilliseconds;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        progress?.Report((reader.DataEnd, reader.DataEnd, count));
        return new PreviewResult(rows, reader.Header, count, warnings, timer.Elapsed, count > PreviewLimit);
    }

    private static string Hex(ReadOnlyMemory<byte> data, int limit) =>
        string.Join(" ", data.Span[..Math.Min(data.Length, limit)].ToArray().Select(b => b.ToString("X2")))
        + (data.Length > limit ? $" … ({data.Length} bytes)" : "");
    private static string Limit(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
}

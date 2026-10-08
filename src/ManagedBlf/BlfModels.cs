namespace ManagedBlf;

/// <summary>Raw BLF local wall-clock fields; the file does not specify a timezone.</summary>
public readonly record struct BlfSystemTime(ushort Year, ushort Month, ushort DayOfWeek,
    ushort Day, ushort Hour, ushort Minute, ushort Second, ushort Milliseconds)
{
    public DateTime? ToDateTime()
    {
        try
        {
            return new DateTime(Year, Month, Day, Hour, Minute, Second, Milliseconds, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

public sealed record BlfFileHeader
{
    public uint HeaderSize { get; init; }
    public uint ApiNumber { get; init; }
    public byte ApplicationId { get; init; }
    public byte CompressionLevel { get; init; }
    public byte ApplicationMajor { get; init; }
    public byte ApplicationMinor { get; init; }
    public uint ApplicationBuild { get; init; }
    public ulong FileSize { get; init; }
    public ulong UncompressedFileSize { get; init; }
    public uint ObjectCount { get; init; }
    public BlfSystemTime MeasurementStartTime { get; init; }
    public BlfSystemTime LastObjectTime { get; init; }
    public ulong RestorePointsOffset { get; init; }
}

public readonly record struct BlfObjectHeader(ushort HeaderSize, ushort HeaderVersion, uint ObjectSize, uint ObjectType);

/// <summary>A complete on-disk object, without its inter-object padding or native pointer conversion.</summary>
public sealed record BlfObject(BlfObjectHeader Header, ulong? TimestampNanoseconds, ReadOnlyMemory<byte> RawData)
{
    public ReadOnlyMemory<byte> Body => RawData[Header.HeaderSize..];
}

public sealed record BlfFileStatistics(BlfFileHeader Header, ulong ObjectsRead, long FilePosition, long DataEnd);

/// <summary>Limits are checked before allocations. Reader instances are single-consumer.</summary>
public sealed record BlfReaderOptions
{
    public int MaxObjectSize { get; init; } = 16 * 1024 * 1024;
    public int MaxContainerSize { get; init; } = 64 * 1024 * 1024;
    public int MaxBufferedBytes { get; init; } = 80 * 1024 * 1024;

    internal void Validate()
    {
        if (MaxObjectSize < 40 || MaxContainerSize < 32 || MaxBufferedBytes < MaxObjectSize
            || MaxBufferedBytes < MaxContainerSize || MaxBufferedBytes > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(BlfReaderOptions), "Invalid BLF resource limits.");
        }
    }
}

public static class BlfObjectTypes
{
    public const uint CanMessage = 1;
    public const uint LogContainer = 10;
    public const uint LinMessage = 11;
    public const uint LinMessage2 = 57;
    public const uint AppText = 65;
    public const uint CanMessage2 = 86;
    public const uint CanFdMessage = 100;
    public const uint CanFdMessage64 = 101;
    public const uint RestorePoint = 115;

    public static string GetName(uint type) => type switch
    {
        CanMessage => "CAN_MESSAGE",
        CanMessage2 => "CAN_MESSAGE2",
        CanFdMessage => "CAN_FD_MESSAGE",
        CanFdMessage64 => "CAN_FD_MESSAGE_64",
        LinMessage => "LIN_MESSAGE",
        LinMessage2 => "LIN_MESSAGE2",
        AppText => "APP_TEXT",
        RestorePoint => "RESTORE_POINT",
        _ => $"OBJECT_{type}"
    };
}

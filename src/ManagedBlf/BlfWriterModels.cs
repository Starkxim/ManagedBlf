namespace ManagedBlf;

/// <summary>A classic CAN frame with an explicit identifier format and a relative nanosecond time.
/// Data is copied during WriteCanMessage; do not mutate it concurrently with that call.</summary>
public sealed record BlfCanFrame
{
    public long TimestampNanoseconds { get; init; }
    /// <summary>One-based BLF channel, from 1 through 65535.</summary>
    public int Channel { get; init; } = 1;
    /// <summary>Identifier without the BLF extended-ID storage bit.</summary>
    public uint Identifier { get; init; }
    public bool IsExtended { get; init; }
    public bool IsRemote { get; init; }
    public bool IsTransmit { get; init; }
    /// <summary>0 through 8. Remote frames carry no data, even when DLC is nonzero.</summary>
    public int Dlc { get; init; }
    public ReadOnlyMemory<byte> Data { get; init; }
}

/// <summary>Bounded buffering for the new-file, uncompressed CAN writer.</summary>
public sealed record BlfWriterOptions
{
    /// <summary>Maximum container payload bytes (48 through 64 MiB minus 32).
    /// Complete 48-byte CAN objects are packed without splitting; the effective capacity rounds down.</summary>
    public int MaxContainerDataSize { get; init; } = 64 * 1024;

    internal void Validate()
    {
        if (MaxContainerDataSize < 48 || MaxContainerDataSize > 64 * 1024 * 1024 - 32)
            throw new ArgumentOutOfRangeException(nameof(MaxContainerDataSize));
    }
}

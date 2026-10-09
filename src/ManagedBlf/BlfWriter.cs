using System.Buffers.Binary;
using System.Runtime.ExceptionServices;

namespace ManagedBlf;

/// <summary>Creates a BLF file containing uncompressed CAN type 1 objects.
/// Requires an empty writable, seekable stream used exclusively by this writer until completion.
/// Single-consumer: methods and disposal must not run concurrently.</summary>
public sealed class BlfWriter : IDisposable
{
    private const int FileHeaderSize = 144;
    private const int ContainerHeaderSize = 32;
    private const int CanObjectSize = 48;
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly DateTime _measurementStartTime;
    private byte[] _buffer;
    private int _buffered;
    private long _fileSize = FileHeaderSize;
    private long _lastTimestamp;
    private ExceptionDispatchInfo? _fault;
    private bool _disposed;

    /// <summary>Starts a new file. The start time must be a timezone-free (Unspecified),
    /// millisecond-aligned wall clock in years 1601..9999. Ownership transfers only after successful construction.
    /// Input validation throws ArgumentException; stream errors propagate. No existing data is truncated.</summary>
    public BlfWriter(Stream stream, DateTime measurementStartTime, bool leaveOpen = false, BlfWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValidateConfiguration(measurementStartTime, options);
        if (!stream.CanWrite || !stream.CanSeek)
            throw new ArgumentException("An empty writable, seekable stream is required.", nameof(stream));
        if (stream.Length != 0 || stream.Position != 0)
            throw new ArgumentException("The stream must be empty and positioned at zero.", nameof(stream));
        int capacity = (options ?? new BlfWriterOptions()).MaxContainerDataSize;
        _buffer = new byte[capacity / CanObjectSize * CanObjectSize];
        _stream = stream;
        _leaveOpen = leaveOpen;
        _measurementStartTime = measurementStartTime;
        // A provisional header is not a completed file; Complete must flush and backfill it.
        WriteFileHeader(completed: false);
    }

    /// <summary>Creates a new path with FileMode.CreateNew. An existing file is never overwritten.
    /// A construction or later I/O failure may leave a partial new file; creation is not transactional.</summary>
    public static BlfWriter Create(string path, DateTime measurementStartTime, BlfWriterOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ValidateConfiguration(measurementStartTime, options);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try { return new BlfWriter(stream, measurementStartTime, options: options); }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>Frames accepted into the writer; buffering does not imply durable storage.</summary>
    public uint ObjectsWritten { get; private set; }
    public bool IsCompleted { get; private set; }

    /// <summary>Validates and synchronously copies one classic CAN frame.
    /// Argument errors do not fault the writer. Timestamps may be out of order; the last frame sets LastObjectTime.
    /// I/O failures permanently fault the writer and are rethrown by subsequent write/completion calls.</summary>
    public void WriteCanMessage(BlfCanFrame frame)
    {
        CheckState();
        if (IsCompleted) throw new InvalidOperationException("The BLF file is already complete.");
        ArgumentNullException.ThrowIfNull(frame);
        ValidateFrame(frame);
        if (ObjectsWritten == uint.MaxValue)
            throw new InvalidOperationException("BLF object count exceeds its 32-bit field.");
        // Reserve enough stream address space for this object and its eventual container header.
        _ = checked(_fileSize + ContainerHeaderSize + _buffered + CanObjectSize
            + (_buffered == _buffer.Length ? ContainerHeaderSize : 0));

        Span<byte> raw = stackalloc byte[CanObjectSize];
        raw.Clear();
        "LOBJ"u8.CopyTo(raw);
        U16(raw, 4, 32); U16(raw, 6, 1); U32(raw, 8, CanObjectSize); U32(raw, 12, 1);
        U32(raw, 16, 2); U64(raw, 24, (ulong)frame.TimestampNanoseconds);
        U16(raw, 32, (ushort)frame.Channel);
        raw[34] = (byte)((frame.IsRemote ? 0x80 : 0) | (frame.IsTransmit ? 1 : 0));
        raw[35] = (byte)frame.Dlc;
        U32(raw, 36, frame.Identifier | (frame.IsExtended ? 0x80000000U : 0));
        frame.Data.Span.CopyTo(raw[40..]);
        try
        {
            if (_buffered == _buffer.Length) FlushContainer();
            raw.CopyTo(_buffer.AsSpan(_buffered));
            _buffered += CanObjectSize;
            ObjectsWritten++;
            _lastTimestamp = frame.TimestampNanoseconds;
        }
        catch (Exception error) { _fault = ExceptionDispatchInfo.Capture(error); throw; }
    }

    /// <summary>Flushes containers, backfills size/count/wall-clock metadata, then flushes the stream.
    /// Repeated successful completion is a no-op until disposal. Flush is not a disk durability guarantee.</summary>
    public void Complete()
    {
        CheckState();
        if (IsCompleted) return;
        try
        {
            FlushContainer();
            _stream.Position = 0;
            WriteFileHeader(completed: true);
            _stream.Position = _fileSize;
            _stream.Flush();
            IsCompleted = true;
        }
        catch (Exception error) { _fault = ExceptionDispatchInfo.Capture(error); throw; }
    }

    /// <summary>Completes a healthy writer, then closes an owned stream even when completion fails.
    /// A previously faulted writer is not finalized or retried. Disposal is idempotent.
    /// With leaveOpen the stream stays open; all writer operations still become invalid.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        try { if (_fault is null) Complete(); }
        finally
        {
            _disposed = true;
            _buffer = Array.Empty<byte>();
            if (!_leaveOpen) _stream.Dispose();
        }
    }

    private static void ValidateConfiguration(DateTime start, BlfWriterOptions? options)
    {
        (options ?? new BlfWriterOptions()).Validate();
        if (start.Kind != DateTimeKind.Unspecified || start.Year < 1601
            || start.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ArgumentException("Measurement start must be Unspecified, millisecond-aligned and in years 1601..9999.", nameof(start));
    }

    private void ValidateFrame(BlfCanFrame frame)
    {
        if (frame.Channel is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(frame.Channel));
        if (frame.Identifier > (frame.IsExtended ? 0x1FFFFFFFU : 0x7FFU))
            throw new ArgumentOutOfRangeException(nameof(frame.Identifier));
        if (frame.Dlc is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(frame.Dlc));
        if (frame.Data.Length != (frame.IsRemote ? 0 : frame.Dlc))
            throw new ArgumentException("Payload must match DLC exactly, or be empty for an RTR frame.", nameof(frame.Data));
        if (frame.TimestampNanoseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(frame.TimestampNanoseconds));
        long ticks = frame.TimestampNanoseconds / 100;
        long remaining = DateTime.MaxValue.Ticks - _measurementStartTime.Ticks;
        if (ticks > remaining || (ticks == remaining && frame.TimestampNanoseconds % 100 != 0))
            throw new ArgumentOutOfRangeException(nameof(frame.TimestampNanoseconds), "Timestamp overflows the measurement wall clock.");
    }

    private void CheckState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _fault?.Throw();
    }

    private void FlushContainer()
    {
        if (_buffered == 0) return;
        Span<byte> header = stackalloc byte[ContainerHeaderSize];
        header.Clear();
        "LOBJ"u8.CopyTo(header);
        U16(header, 4, 16); U16(header, 6, 1);
        U32(header, 8, (uint)(ContainerHeaderSize + _buffered)); U32(header, 12, 10);
        U32(header, 24, (uint)_buffered);
        _stream.Write(header);
        _stream.Write(_buffer.AsSpan(0, _buffered));
        _fileSize = checked(_fileSize + ContainerHeaderSize + _buffered);
        _buffered = 0;
    }

    private void WriteFileHeader(bool completed)
    {
        Span<byte> header = stackalloc byte[FileHeaderSize];
        header.Clear();
        "LOGG"u8.CopyTo(header); U32(header, 4, FileHeaderSize);
        // API/application identity, compression level, restore points and reserved fields are unknown/zero.
        if (completed)
        {
            U64(header, 16, (ulong)_fileSize);
            // For compression 0: 144 + sum(container header 32 + expanded object bytes).
            U64(header, 24, (ulong)_fileSize);
            U32(header, 32, ObjectsWritten);
        }
        SystemTime(header[40..], _measurementStartTime);
        SystemTime(header[56..], _measurementStartTime.AddTicks(_lastTimestamp / 100));
        _stream.Write(header);
    }

    private static void SystemTime(Span<byte> raw, DateTime value)
    {
        U16(raw, 0, (ushort)value.Year); U16(raw, 2, (ushort)value.Month);
        U16(raw, 4, (ushort)value.DayOfWeek); U16(raw, 6, (ushort)value.Day);
        U16(raw, 8, (ushort)value.Hour); U16(raw, 10, (ushort)value.Minute);
        U16(raw, 12, (ushort)value.Second); U16(raw, 14, (ushort)value.Millisecond);
    }

    private static void U16(Span<byte> raw, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(raw[offset..], value);
    private static void U32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);
    private static void U64(Span<byte> raw, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(raw[offset..], value);
}

using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.ExceptionServices;

namespace ManagedBlf;

/// <summary>
/// Managed BLF object stream reader, adapted from the author's original BlfReader.cs.
/// Strict container validation; false means clean EOF. Malformed input throws InvalidDataException.
/// Instances are not thread-safe. Dispose before replacing a file.
/// </summary>
public sealed class BlfReader : IDisposable
{
    public const uint FileSignature = 0x47474F4C;
    public const uint ObjectSignature = 0x4A424F4C;
    public const int FileHeaderSize = 144;
    private const int MinimumFileHeaderSize = 80;
    private const int BaseHeaderSize = 16;
    private const int ContainerHeaderSize = 32;
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly BlfReaderOptions _options;
    private readonly long _dataEnd;
    private long _position;
    private byte[] _buffer;
    private int _head;
    private int _tail;
    private BlfObjectHeader? _peeked;
    private ExceptionDispatchInfo? _fault;
    private bool _disposed;

    public BlfReader(Stream stream, bool leaveOpen = false, BlfReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("A readable, seekable stream is required.", nameof(stream));
        }
        _options = options ?? new BlfReaderOptions();
        _options.Validate();
        _buffer = new byte[Math.Min(128 * 1024, _options.MaxBufferedBytes)];
        _stream = stream;
        _leaveOpen = leaveOpen;
        if (stream.Length < MinimumFileHeaderSize)
        {
            throw new InvalidDataException("Truncated BLF file header.");
        }
        Span<byte> raw = stackalloc byte[MinimumFileHeaderSize];
        stream.Position = 0;
        stream.ReadExactly(raw);
        if (U32(raw, 0) != FileSignature)
        {
            throw new InvalidDataException("Expected BLF signature LOGG.");
        }
        Header = ParseHeader(raw);
        if (Header.HeaderSize < MinimumFileHeaderSize || Header.HeaderSize > stream.Length)
        {
            throw new InvalidDataException("Invalid BLF file header size.");
        }
        _position = Header.HeaderSize;
        _dataEnd = stream.Length;
        if (Header.ApiNumber >= 4010600 && Header.RestorePointsOffset != 0)
        {
            if (Header.RestorePointsOffset < Header.HeaderSize || Header.RestorePointsOffset > (ulong)stream.Length)
            {
                throw new InvalidDataException("Restore point offset lies outside the data area.");
            }
            _dataEnd = (long)Header.RestorePointsOffset;
        }
    }

    public BlfFileHeader Header { get; }
    public ulong ObjectsRead { get; private set; }
    public long FilePosition => _position;
    public long DataEnd => _dataEnd;

    public static BlfReader Open(string path, BlfReaderOptions? options = null)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        try
        {
            return new BlfReader(stream, options: options);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public bool PeekObject(out BlfObjectHeader header)
    {
        CheckState();
        if (_peeked is not { } next)
        {
            if (!EnsureAvailable(BaseHeaderSize))
            {
                if (_tail != _head)
                {
                    Fail("Truncated object header at end of object stream.");
                }
                header = default;
                return false;
            }
            ReadOnlySpan<byte> raw = _buffer.AsSpan(_head, BaseHeaderSize);
            if (U32(raw, 0) != ObjectSignature)
            {
                Fail("Expected object signature LOBJ.");
            }
            next = new BlfObjectHeader(U16(raw, 4), U16(raw, 6), U32(raw, 8), U32(raw, 12));
            ValidateObjectHeader(next);
            _peeked = next;
        }
        header = next;
        return true;
    }

    /// <summary>Copies serialized bytes into a bounded destination, zeroing its unused suffix.
    /// A short destination intentionally truncates the copy; the entire object is consumed.</summary>
    public bool ReadObject(Span<byte> destination)
    {
        if (!PeekObject(out var header))
        {
            return false;
        }
        EnsureCompleteObject(header);
        int copied = Math.Min((int)header.ObjectSize, destination.Length);
        _buffer.AsSpan(_head, copied).CopyTo(destination);
        destination[copied..].Clear();
        ConsumeObject(header);
        return true;
    }

    /// <summary>Reads one complete serialized object. Unknown types remain available as raw bytes.</summary>
    public bool ReadNext(out BlfObject? value)
    {
        value = null;
        if (!PeekObject(out var header))
        {
            return false;
        }
        EnsureCompleteObject(header);
        ulong? timestamp = PeekTimestamp(out var ns) ? ns : null;
        byte[] raw = _buffer.AsSpan(_head, (int)header.ObjectSize).ToArray();
        ConsumeObject(header);
        value = new BlfObject(header, timestamp, raw);
        return true;
    }

    public bool SkipObject()
    {
        if (!PeekObject(out var header))
        {
            return false;
        }
        EnsureCompleteObject(header);
        ConsumeObject(header);
        return true;
    }

    /// <summary>Version 1/2 timestamps in nanoseconds relative to measurement start.
    /// Unknown header versions or time resolutions return false, without consuming the object.</summary>
    public bool PeekTimestamp(out ulong timestamp)
    {
        timestamp = 0;
        if (!PeekObject(out var header) || header.HeaderVersion is not (1 or 2))
        {
            return false;
        }
        if (!EnsureAvailable(header.HeaderSize))
        {
            Fail("Truncated timestamp header.");
        }
        ReadOnlySpan<byte> raw = _buffer.AsSpan(_head, header.HeaderSize);
        uint resolution = U32(raw, 16) & 3;
        if (resolution is not (1 or 2))
        {
            return false;
        }
        ulong value = U64(raw, 24);
        if (resolution == 1 && value > ulong.MaxValue / 10000)
        {
            Fail("10 microsecond timestamp overflows nanosecond range.");
        }
        timestamp = resolution == 1 ? value * 10000 : value;
        return true;
    }

    /// <summary>Legacy binlog type-specific padding, measured by the author.
    /// This is size modulo four, not a generic alignment formula.</summary>
    public static bool HasPadding(uint type) => type is
        6 or 7 or 8 or 9 or 10 or 32 or 33 or 65 or 69 or 71 or 72 or
        76 or 77 or 78 or 79 or 80 or 81 or 83 or 84 or 85 or 90 or 92 or 93 or
        96 or 97 or 102 or 117;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _buffer = Array.Empty<byte>();
        if (!_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    private void CheckState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _fault?.Throw();
    }

    private void Fail(string message)
    {
        var error = new InvalidDataException($"{message} (top-level file position {_position}).");
        _fault = ExceptionDispatchInfo.Capture(error);
        throw error;
    }

    private void ValidateObjectHeader(BlfObjectHeader header)
    {
        if (header.HeaderSize < BaseHeaderSize || header.HeaderSize > header.ObjectSize
            || header.ObjectSize > _options.MaxObjectSize
            || (header.HeaderVersion == 1 && header.HeaderSize < 32)
            || (header.HeaderVersion == 2 && header.HeaderSize < 40))
        {
            Fail("Invalid or oversized object header.");
        }
    }

    private void EnsureCompleteObject(BlfObjectHeader header)
    {
        if (!EnsureAvailable((int)header.ObjectSize))
        {
            Fail("Truncated object payload.");
        }
    }

    private void ConsumeObject(BlfObjectHeader header)
    {
        _peeked = null;
        _head += (int)header.ObjectSize;
        if (header.ObjectType != BlfObjectTypes.RestorePoint)
        {
            ObjectsRead++;
        }
        int padding = HasPadding(header.ObjectType) ? (int)(header.ObjectSize % 4) : 0;
        if (padding != 0)
        {
            if (!EnsureAvailable(padding))
            {
                // Some writers omit padding after the very last object; no next object is lost.
                _head = _tail;
                return;
            }
            _head += padding;
        }
    }

    private bool EnsureAvailable(int count)
    {
        CheckState();
        try
        {
            while (_tail - _head < count)
            {
                if (!AppendNextTopLevelObject())
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            _fault ??= ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }

    private bool AppendNextTopLevelObject()
    {
        Span<byte> raw = stackalloc byte[ContainerHeaderSize];
        while (_position < _dataEnd)
        {
            long start = _position;
            if (_dataEnd - start < BaseHeaderSize)
            {
                Fail("Truncated top-level object header.");
            }
            _stream.Position = start;
            _stream.ReadExactly(raw[..BaseHeaderSize]);
            uint size = U32(raw, 8);
            uint type = U32(raw, 12);
            ushort headerSize = U16(raw, 4);
            if (U32(raw, 0) != ObjectSignature || headerSize < BaseHeaderSize || headerSize > size
                || size > _options.MaxContainerSize || size > _dataEnd - start)
            {
                Fail("Invalid top-level object size or signature.");
            }
            int padding = HasPadding(type) ? (int)(size % 4) : 0;
            _position = Math.Min(start + size + padding, _dataEnd);
            if (type != BlfObjectTypes.LogContainer)
            {
                if (size > _options.MaxObjectSize)
                {
                    Fail("Top-level object exceeds object size limit.");
                }
                _stream.Position = start;
                AppendFromStream((int)(_position - start));
                return true;
            }
            if (headerSize != BaseHeaderSize || size < ContainerHeaderSize)
            {
                Fail("Unsupported or truncated LOG_CONTAINER header.");
            }
            _stream.ReadExactly(raw[BaseHeaderSize..]);
            AppendContainer(U16(raw, 16), (int)size - ContainerHeaderSize, U32(raw, 24));
            if (_tail > _head)
            {
                return true;
            }
        }
        return false;
    }

    private void AppendContainer(ushort compression, int payloadLength, uint declaredSize)
    {
        if (compression is not (0 or 2))
        {
            Fail($"Unsupported LOG_CONTAINER compression method {compression}.");
        }
        if (declaredSize > _options.MaxContainerSize)
        {
            Fail("Container expanded size exceeds the configured limit.");
        }
        if (compression == 0)
        {
            if (declaredSize != payloadLength)
            {
                Fail("Uncompressed container size does not match its payload.");
            }
            AppendFromStream(payloadLength);
            return;
        }
        byte[] payload = new byte[payloadLength];
        _stream.ReadExactly(payload);
        Inflate(payload, (int)declaredSize);
    }

    private void Inflate(byte[] payload, int declaredSize)
    {
        if (payload.Length < 6 || (payload[0] & 15) != 8 || (payload[0] >> 4) > 7
            || ((payload[0] << 8) | payload[1]) % 31 != 0 || (payload[1] & 32) != 0)
        {
            Fail("Invalid zlib header or unsupported preset dictionary.");
        }
        ReserveTail(declaredSize);
        int start = _tail;
        using var input = new MemoryStream(payload, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        try
        {
            zlib.ReadExactly(_buffer.AsSpan(start, declaredSize));
            if (zlib.ReadByte() != -1)
            {
                Fail("Container expands beyond its declared size.");
            }
        }
        catch (EndOfStreamException)
        {
            Fail("Container expands to fewer bytes than declared.");
        }
        uint expected = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(payload.Length - 4));
        uint a = 1, b = 0;
        foreach (byte item in _buffer.AsSpan(start, declaredSize))
        {
            a = (a + item) % 65521;
            b = (b + a) % 65521;
        }
        if (((b << 16) | a) != expected)
        {
            Fail("Container Adler-32 checksum mismatch.");
        }
        _tail = start + declaredSize;
    }

    private void AppendFromStream(int count)
    {
        ReserveTail(count);
        _stream.ReadExactly(_buffer.AsSpan(_tail, count));
        _tail += count;
    }

    private void ReserveTail(int count)
    {
        int pending = _tail - _head;
        if ((long)pending + count > _options.MaxBufferedBytes)
        {
            Fail("Buffered object stream exceeds the configured limit.");
        }
        if (_head != 0)
        {
            Buffer.BlockCopy(_buffer, _head, _buffer, 0, pending);
            _head = 0;
            _tail = pending;
        }
        int required = _tail + count;
        if (required > _buffer.Length)
        {
            Array.Resize(ref _buffer, (int)Math.Min(_options.MaxBufferedBytes,
                Math.Max(required, (long)_buffer.Length * 2)));
        }
    }

    private static BlfFileHeader ParseHeader(ReadOnlySpan<byte> raw) => new()
    {
        HeaderSize = U32(raw, 4), ApiNumber = U32(raw, 8),
        ApplicationId = raw[12], CompressionLevel = raw[13], ApplicationMajor = raw[14], ApplicationMinor = raw[15],
        FileSize = U64(raw, 16), UncompressedFileSize = U64(raw, 24),
        ObjectCount = U32(raw, 32), ApplicationBuild = U32(raw, 36),
        MeasurementStartTime = ReadSystemTime(raw[40..]), LastObjectTime = ReadSystemTime(raw[56..]),
        RestorePointsOffset = U64(raw, 72)
    };

    private static BlfSystemTime ReadSystemTime(ReadOnlySpan<byte> raw) => new(
        U16(raw, 0), U16(raw, 2), U16(raw, 4), U16(raw, 6), U16(raw, 8), U16(raw, 10), U16(raw, 12), U16(raw, 14));
    private static ushort U16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static ulong U64(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(raw[offset..]);
}

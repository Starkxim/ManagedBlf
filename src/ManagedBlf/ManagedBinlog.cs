using System.Collections.Concurrent;

namespace ManagedBlf;

/// <summary>
/// Optional handle facade inspired by the author's original ManagedBinlog.cs.
/// This is a safe managed API, not a native ABI replacement. Serialized object bytes are not native structs.
/// Operations on one handle are serialized. Prefer a using-scoped BlfReader for new applications.
/// </summary>
public static class ManagedBinlog
{
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);
    public const uint GENERIC_READ = 0x80000000;
    private static readonly ConcurrentDictionary<IntPtr, Session> Readers = new();
    private static long _lastHandle;

    private sealed class Session(BlfReader reader)
    {
        public readonly object Gate = new();
        public BlfReader Reader { get; } = reader;
        public bool Closed { get; set; }
    }

    /// <summary>Read access only. Open failures are returned with an actionable exception.</summary>
    public static IntPtr BLCreateFile(string path, uint desiredAccess, out Exception? error)
    {
        error = null;
        if (desiredAccess != GENERIC_READ)
        {
            error = new NotSupportedException("Only GENERIC_READ is supported.");
            return INVALID_HANDLE_VALUE;
        }
        BlfReader reader;
        try
        {
            reader = BlfReader.Open(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            error = ex;
            return INVALID_HANDLE_VALUE;
        }
        long id = Interlocked.Increment(ref _lastHandle);
        if (id <= 0 || (IntPtr.Size == 4 && id > int.MaxValue))
        {
            reader.Dispose();
            error = new InvalidOperationException("Managed handle range exhausted.");
            return INVALID_HANDLE_VALUE;
        }
        var handle = new IntPtr(id);
        Readers[handle] = new Session(reader);
        return handle;
    }

    public static int BLCloseHandle(IntPtr handle)
    {
        if (!Readers.TryRemove(handle, out var session))
        {
            return 0;
        }
        lock (session.Gate)
        {
            session.Closed = true;
            session.Reader.Dispose();
            return 1;
        }
    }

    public static bool BLPeekObject(IntPtr handle, out BlfObjectHeader header)
    {
        header = default;
        if (!Readers.TryGetValue(handle, out var session)) return false;
        lock (session.Gate)
        {
            return !session.Closed && session.Reader.PeekObject(out header);
        }
    }

    public static bool BLPeekTimestamp(IntPtr handle, out ulong nanoseconds)
    {
        nanoseconds = 0;
        if (!Readers.TryGetValue(handle, out var session)) return false;
        lock (session.Gate)
        {
            return !session.Closed && session.Reader.PeekTimestamp(out nanoseconds);
        }
    }

    /// <summary>Writes only within the supplied span. Raw file bytes; no APP_TEXT pointer conversion.</summary>
    public static bool BLReadObjectSecure(IntPtr handle, Span<byte> destination)
    {
        if (!Readers.TryGetValue(handle, out var session)) return false;
        lock (session.Gate)
        {
            return !session.Closed && session.Reader.ReadObject(destination);
        }
    }

    public static bool BLSkipObject(IntPtr handle)
    {
        if (!Readers.TryGetValue(handle, out var session)) return false;
        lock (session.Gate)
        {
            return !session.Closed && session.Reader.SkipObject();
        }
    }

    /// <summary>No unmanaged allocation exists to free. Retained only for migration convenience.</summary>
    public static int BLFreeObject(IntPtr handle) => Readers.ContainsKey(handle) ? 1 : 0;

    public static bool BLGetFileStatisticsEx(IntPtr handle, out BlfFileStatistics? statistics)
    {
        statistics = null;
        if (!Readers.TryGetValue(handle, out var session)) return false;
        lock (session.Gate)
        {
            if (session.Closed) return false;
            var reader = session.Reader;
            statistics = new BlfFileStatistics(reader.Header, reader.ObjectsRead, reader.FilePosition, reader.DataEnd);
            return true;
        }
    }
}

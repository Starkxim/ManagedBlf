# ManagedBlf

A small, independent C# reader for Vector BLF (Binary Logging Format) files, with a Windows viewer for trying the parser. The core library targets .NET 8 and has no external package or native DLL dependencies. The viewer uses standard Windows Forms.

[中文说明](README.zh-CN.md) · [Format and API boundaries](docs/FORMAT.md) · [Manual acceptance cases](docs/MANUAL-CHECKS.md) · [Source provenance](docs/PROVENANCE.md)

## Layout

- `src/ManagedBlf`: independently usable sequential reader, managed models, safe handle facade, and a limited message decoder.
- `samples/ManagedBlf.Viewer`: GUI with open, cancel, filter, raw object view, and synthetic demo generation.

Both belong in one repository: the demo has a project reference to the library and demonstrates the same published API. No GUI dependency enters the library.

## Build and run

Install the .NET 8 SDK or a newer SDK capable of targeting .NET 8. Running requires the .NET 8 runtime; the viewer requires Windows and the .NET 8 Desktop Runtime.

```powershell
dotnet build ManagedBlf.sln -c Release
dotnet run --project samples/ManagedBlf.Viewer -c Release
```

On another operating system, build only the platform-independent core:

```sh
dotnet build src/ManagedBlf/ManagedBlf.csproj -c Release
```

Choose **Load demo** for seven synthetic CAN, CAN FD, LIN, and text objects, including one split across a compressed and an uncompressed container. **Save demo** writes that generated fixture to your chosen path; no business logs or proprietary binaries are bundled.

The viewer scans the whole file on a background task and retains the first 10,000 objects as a bounded preview. Filtering searches that preview. Selecting a row shows up to 256 serialized bytes and decoder warnings. Closing or cancelling requests cancellation between objects; an in-progress object read/decompression operation is not interruptible.

## Library use

```csharp
using ManagedBlf;

using var reader = BlfReader.Open("capture.blf");
while (reader.ReadNext(out var item))
{
    Console.WriteLine($"{item!.Header.ObjectType}: {item.TimestampNanoseconds} ns");
    if (BlfMessageDecoder.TryDecode(item, out var message))
        Console.WriteLine($"{message!.Protocol} {message.Identifier:X} {Convert.ToHexString(message.Data.Span)}");
    // item.RawData contains the serialized object, including the full object header.
}
```

`false` means clean EOF. Corrupt or unsupported containers throw; file I/O errors propagate. `using` ensures file release on cancellation, exceptions, and early return. A reader instance is single-consumer. The optional `ManagedBinlog` facade serializes operations on a handle, but a peek/read pair is not an atomic transaction: use one consumer per handle.

The facade accepts a bounded `Span<byte>`. Its signatures and data model are **not native ABI-compatible**. `APP_TEXT` is returned as serialized bytes or decoded managed text, never a native pointer. Unknown object types remain readable as raw objects, subject to the documented legacy padding table.

## Current scope

- Read-only LOGG/LOBJ object stream; uncompressed and zlib containers; objects spanning containers.
- Nanosecond normalization of header versions 1 and 2, including 10 microsecond resolution. File wall-clock fields do not establish a timezone.
- Message views: CAN 1/86, CAN FD 100/101, LIN 11/57, APP_TEXT 65. Other event/error objects remain raw.
- Strict zlib header, Adler-32, expanded length, object bounds, and configurable memory limits.
- No general writer, native DLL replacement, recovery/index seeking, DBC decoding, hardware capture, or claim of full BLF compatibility.

Third-party format observations were consulted as references; their implementation source is not included. This project is not affiliated with or endorsed by Vector Informatik.

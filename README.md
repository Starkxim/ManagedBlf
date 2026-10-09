# ManagedBlf

A small, independent C# reader for Vector BLF (Binary Logging Format) files, with a Windows viewer for trying the parser. The core library targets .NET 8 and .NET 10 and has no external package or native DLL dependencies. The viewer uses standard Windows Forms.

[中文说明](README.zh-CN.md) · [Format and API boundaries](docs/FORMAT.md) · [Manual acceptance cases](docs/MANUAL-CHECKS.md) · [Source provenance](docs/PROVENANCE.md)

## Layout

- `src/ManagedBlf`: independently usable sequential reader, managed models, safe handle facade, and a limited message decoder.
- `samples/ManagedBlf.Viewer`: GUI with open, cancel, filter, raw object view, and synthetic demo generation.

Both belong in one repository: the demo has a project reference to the library and demonstrates the same published API. No GUI dependency enters the library.

## Build and run

Install the .NET 10 SDK. The core retains net8.0 consumer compatibility and adds net10.0; running uses the corresponding runtime. The viewer now targets net10.0-windows and requires Windows with the .NET 10 Desktop Runtime. Existing .NET 8 applications can continue referencing the net8.0 core without an API change.

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

## Automated validation

```sh
dotnet test tests/ManagedBlf.Tests/ManagedBlf.Tests.csproj -c Release
```

Install both .NET 8 and .NET 10 runtimes to execute both test targets. Fixtures are independently authored synthetic bytes; see [fixture provenance](tests/ManagedBlf.Tests/README.md). CI builds/tests the core on Linux and Windows for each target and separately builds the viewer on Windows; it does not verify GUI interaction.

As checked on 2026-10-09, [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) ends .NET 8 support on 2026-11-10 and supports .NET 10 LTS through 2028-11-14. net8.0 is retained for compatibility, not as an extension of Microsoft support; prefer net10.0 for new consumers. No third-party/native dependencies were added to the core.

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

## Support matrix

| Capability | Status |
| --- | --- |
| Sequential LOGG/LOBJ; compression 0/2; cross-container objects; bounded Span/handle API | Implemented |
| Header v1/v2 flag 1/2 timestamps; CAN 1/86, FD 100/101, LIN 11/57, APP_TEXT 65 views | Implemented within documented layouts |
| Unknown events/errors, header v3, FD101 extension attributes | Raw objects only; future padding is not guaranteed |
| Writer, indexing/seek, broader typed events, NuGet and viewer downloads | Planned |

## Roadmap / To-do

Completed boxes describe implemented scope; planned validation stays unchecked until executed. See [format boundaries](docs/FORMAT.md) and [manual checks](docs/MANUAL-CHECKS.md).

0. [x] Publish this ordered roadmap and support matrix in both READMEs. Acceptance: matching public documentation with existing usage and license retained.
1. [x] Independent regression tests and CI. Build fixtures from literal format fields, independently of production reader/demo logic; document public synthetic provenance. Cover EOF/errors, signatures/truncation, compression 0/2 and cross-container objects, zlib header/checksum/length, resource bounds, v1/v2 units/overflow, raw preservation, Span bounds, peek/skip/handle lifecycle, all seven decoders and FD101 short/extension variants. Acceptance: actual Linux and Windows Release build/test Actions runs, reported test counts, and a separate Windows GUI build. Assess .NET 10 LTS against Microsoft's current support policy while explaining .NET 8 consumer compatibility. GUI interaction remains separate manual acceptance.
2. [ ] Windows viewer v0.1.0-alpha downloads. Provide LICENSE, usage and locally generated synthetic demo; identify framework-dependent and self-contained packages and runtime requirements. Use a separate manual/tag release workflow with write permission only on its release job. Acceptance: inspected clean package contents and packaging/startup checks; label outstanding Windows interaction checks explicitly.
3. [ ] Minimal safe writer for new files: writable/seekable streams, LOGG, LOBJ v1 nanosecond timestamps, uncompressed containers and CAN 1. Document ownership/leaveOpen, completion/disposal/fault behavior and format-defined size/count/time metadata. Validate explicit standard/extended ID flags, RTR, DLC/payload, channel and time without truncation or invented data. Acceptance: empty/multiple-object/multiple-container/invalid-input tests, typed round trips, independent expected bytes and an independent BLF tool check. No append, recovery or native pointer ABI.
4. [ ] Extend writer one format at a time: zlib, CAN FD 100/101, LIN 11/57, APP_TEXT 65. Acceptance for each: boundary regression, format evidence, support matrix and example. FD101 extensions and header v3 stay raw until documented evidence and tests exist. Map native capabilities to safe managed APIs; prioritize event/error decoding, indexing and performance using public samples and demand, without promising full BLF coverage.
5. [ ] Stabilize API, documentation and packaging. Document public API ownership, exceptions, threading and lifecycle; keep GUI independent. Acceptance: synchronized READMEs/format/manual checks, local NuGet pack and consumption with the custom LICENSE. Public NuGet publication and third-party commercial relicensing policy require a separately reviewed decision; no contributor relicensing rights are presumed.

Linux regression on 2026-10-09: 58 tests passed on each of net8.0 and net10.0 (zero failures/skips). [Actions run 37874151520](https://github.com/Starkxim/ManagedBlf/actions/runs/37874151520) passed Linux/Windows net8.0/net10.0 (58 tests per job) and the separate Windows viewer Release build. GUI interaction remains pending.

Validation baseline: independent regression and the above Actions run have passed. Previous synthetic checks and Windows build/publish do not establish GUI interaction, macOS/Linux functional regression or complete external interoperability.

## License

ManagedBlf is source-available under the [ManagedBlf Noncommercial Source License 1.0](LICENSE). Copyright (c) 2026 Cao (Starkxim).

- Noncommercial use is free, with copyright and license notices preserved.
- Private noncommercial use does not require publishing changes. When distributing a program or providing an online service using the library, publish the corresponding library source and all library modifications. Your entire application does not need to be published.
- Commercial use requires a separate written paid license, including company internal tools, commissioned work, and commercial research or development, even if the application is not sold or distributed.

For commercial licensing, contact [Cao (Starkxim)](https://github.com/Starkxim). Public source code does not grant free commercial permission. This custom license is not an OSI-approved open-source license; the full LICENSE text controls.

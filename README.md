# ManagedBlf

A small, independent C# library for reading Vector BLF (Binary Logging Format) files and writing a limited CAN subset, with a Windows viewer for trying the parser. The core library targets .NET 8 and .NET 10 and has no external package or native DLL dependencies. The viewer uses standard Windows Forms.

[中文说明](README.zh-CN.md) · [Format and API boundaries](docs/FORMAT.md) · [Manual acceptance cases](docs/MANUAL-CHECKS.md) · [Source provenance](docs/PROVENANCE.md)

## Layout

- `src/ManagedBlf`: independently usable sequential reader, minimal CAN writer, managed models, safe handle facade, and a limited message decoder.
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

## Viewer alpha downloads

[v0.1.0-alpha Windows x64 prerelease](https://github.com/Starkxim/ManagedBlf/releases/tag/v0.1.0-alpha) is available with two ZIPs and [SHA256SUMS.txt](https://github.com/Starkxim/ManagedBlf/releases/download/v0.1.0-alpha/SHA256SUMS.txt):

| Download | Runtime requirement | Size |
| --- | --- | --- |
| [Framework-dependent](https://github.com/Starkxim/ManagedBlf/releases/download/v0.1.0-alpha/ManagedBlf.Viewer-0.1.0-alpha-win-x64-framework-dependent.zip) | Install .NET 10 Desktop Runtime x64 | 153,179 bytes |
| [Self-contained](https://github.com/Starkxim/ManagedBlf/releases/download/v0.1.0-alpha/ManagedBlf.Viewer-0.1.0-alpha-win-x64-self-contained.zip) | Includes .NET 10.0.12 and Windows Desktop runtime | 51,549,520 bytes |

Extract the entire ZIP and run `ManagedBlf.Viewer.exe`. Both include LICENSE, bilingual usage instructions and the manual acceptance checklist. Apphost notices are included in `third-party-licenses`; the self-contained ZIP also includes its actual runtime licenses/notices under their own terms. **Load demo** generates synthetic data in memory; `--save-demo` creates a new synthetic file without overwriting an existing path. [Exact corresponding source](https://github.com/Starkxim/ManagedBlf/tree/v0.1.0-alpha) is commit `8ec6a72`. Those existing alpha downloads contain the reader/viewer, without the writer added to current source.

[Release workflow run 37877253845](https://github.com/Starkxim/ManagedBlf/actions/runs/37877253845) passed Windows net8.0/net10.0 regression (58 tests each), both package content checks, demo generation/overwrite protection, and actual window startup. On 2026-10-09 both published ZIPs were downloaded and checked for ZIP integrity, matching SHA-256, unchanged license text, runtime configuration, excluded content and third-party notice bytes against the official 10.0.12 NuGet packs. No native `binlog.dll`, captured logs, proprietary source, credentials or source build directories are bundled. Windows GUI mouse interaction remains unverified; startup checks do not complete manual acceptance. See [download/package instructions](docs/VIEWER-DOWNLOADS.md).

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

## Write a new CAN file

```csharp
using ManagedBlf;

// SYSTEMTIME has no timezone: supply the intended wall clock explicitly.
var start = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Unspecified);
using var writer = BlfWriter.Create("new-capture.blf", start);
writer.WriteCanMessage(new BlfCanFrame
{
    TimestampNanoseconds = 1_000_000,
    Channel = 1,
    Identifier = 0x42,
    IsExtended = true, // Explicit format flag, even for a small identifier.
    IsTransmit = true,
    Dlc = 2,
    Data = new byte[] { 0x12, 0x34 }
});
writer.Complete();
```

`Create` uses `FileMode.CreateNew` and never overwrites an existing path. The stream constructor requires an empty writable/seekable stream at position zero; `leaveOpen: true` keeps it open after disposal. Start time must be `Unspecified`, millisecond-aligned, and in years 1601–9999. Relative nanoseconds must be nonnegative and fit the resulting wall clock. Channels are 1–65535, DLC 0–8, IDs 11 or 29 bits according to `IsExtended`. Data must match DLC exactly; RTR (`IsRemote`) requires empty data while preserving DLC. No caller payload is truncated or fabricated.

The writer buffers complete CAN 1 objects in bounded uncompressed containers. `Complete` flushes data and backfills size/count/time metadata; repeated successful completion is a no-op before disposal. `Dispose` completes a healthy writer and closes its owned stream, including when completion fails. Argument errors allow correction; I/O errors permanently fault the writer, with no finalization retry on disposal. A failure may leave a partial file; creation is not transactional and flush does not guarantee disk durability. Use one consumer and do not mutate the stream concurrently. See [writer format/API details](docs/FORMAT.md#minimal-writer).

## Current scope

- Sequential LOGG/LOBJ reading; uncompressed and zlib containers; objects spanning containers.
- Nanosecond normalization of header versions 1 and 2, including 10 microsecond resolution. File wall-clock fields do not establish a timezone.
- Message views: CAN 1/86, CAN FD 100/101, LIN 11/57, APP_TEXT 65. Other event/error objects remain raw.
- Strict zlib header, Adler-32, expanded length, object bounds, and configurable memory limits.
- New-file writing of CAN 1, LOBJ v1 nanosecond timestamps and uncompressed containers; no append or writer support for other message types yet.
- No native DLL replacement, recovery/index seeking, DBC decoding, hardware capture, or claim of full BLF compatibility.

Third-party format observations were consulted as references; their implementation source is not included. This project is not affiliated with or endorsed by Vector Informatik.

## Support matrix

| Capability | Status |
| --- | --- |
| Sequential LOGG/LOBJ; compression 0/2; cross-container objects; bounded Span/handle API | Implemented |
| Header v1/v2 flag 1/2 timestamps; CAN 1/86, FD 100/101, LIN 11/57, APP_TEXT 65 views | Implemented within documented layouts |
| Unknown events/errors, header v3, FD101 extension attributes | Raw objects only; future padding is not guaranteed |
| Windows x64 alpha viewer downloads | Released; startup checked; GUI interaction pending |
| New-file CAN 1 writer; LOBJ v1 nanoseconds; uncompressed containers | Implemented in current source; stage 3 validation pending |
| Writer zlib, CAN FD, LIN and APP_TEXT; indexing/seek, broader typed events and NuGet | Planned |

## Roadmap / To-do

Completed boxes describe implemented scope; planned validation stays unchecked until executed. See [format boundaries](docs/FORMAT.md) and [manual checks](docs/MANUAL-CHECKS.md).

0. [x] Publish this ordered roadmap and support matrix in both READMEs. Acceptance: matching public documentation with existing usage and license retained.
1. [x] Independent regression tests and CI. Build fixtures from literal format fields, independently of production reader/demo logic; document public synthetic provenance. Cover EOF/errors, signatures/truncation, compression 0/2 and cross-container objects, zlib header/checksum/length, resource bounds, v1/v2 units/overflow, raw preservation, Span bounds, peek/skip/handle lifecycle, all seven decoders and FD101 short/extension variants. Acceptance: actual Linux and Windows Release build/test Actions runs, reported test counts, and a separate Windows GUI build. Assess .NET 10 LTS against Microsoft's current support policy while explaining .NET 8 consumer compatibility. GUI interaction remains separate manual acceptance.
2. [x] Windows viewer v0.1.0-alpha downloads. Provide LICENSE, usage and locally generated synthetic demo; identify framework-dependent and self-contained packages and runtime requirements. Use a separate manual/tag release workflow with write permission only on its release job. Acceptance: inspected clean package contents and packaging/startup checks; label outstanding Windows interaction checks explicitly.
3. [ ] Minimal safe writer for new files: writable/seekable streams, LOGG, LOBJ v1 nanosecond timestamps, uncompressed containers and CAN 1. Document ownership/leaveOpen, completion/disposal/fault behavior and format-defined size/count/time metadata. Validate explicit standard/extended ID flags, RTR, DLC/payload, channel and time without truncation or invented data. Acceptance: empty/multiple-object/multiple-container/invalid-input tests, typed round trips, independent expected bytes and an independent BLF tool check. No append, recovery or native pointer ABI.
4. [ ] Extend writer one format at a time: zlib, CAN FD 100/101, LIN 11/57, APP_TEXT 65. Acceptance for each: boundary regression, format evidence, support matrix and example. FD101 extensions and header v3 stay raw until documented evidence and tests exist. Map native capabilities to safe managed APIs; prioritize event/error decoding, indexing and performance using public samples and demand, without promising full BLF coverage.
5. [ ] Stabilize API, documentation and packaging. Document public API ownership, exceptions, threading and lifecycle; keep GUI independent. Acceptance: synchronized READMEs/format/manual checks, local NuGet pack and consumption with the custom LICENSE. Public NuGet publication and third-party commercial relicensing policy require a separately reviewed decision; no contributor relicensing rights are presumed.

Linux regression on 2026-10-09: 58 tests passed on each of net8.0 and net10.0 (zero failures/skips). [Actions run 37874151520](https://github.com/Starkxim/ManagedBlf/actions/runs/37874151520) passed Linux/Windows net8.0/net10.0 (58 tests per job) and the separate Windows viewer Release build. GUI interaction remains pending.

Stage 3 local Linux regression passed 121 tests per target (58 reader/decoder + 63 writer), with zero failures/skips and zero build warnings/errors. Both net8.0 and net10.0 passed the bidirectional python-can 4.6.1 check and produced identical fixture bytes. Actual Actions acceptance of the new writer jobs is pending. Independent literal-byte expectations and the external check cover the minimal CAN writer only. GUI interaction, macOS regression and complete external BLF interoperability remain unverified.

## License

ManagedBlf is source-available under the [ManagedBlf Noncommercial Source License 1.0](LICENSE). Copyright (c) 2026 Cao (Starkxim).

- Noncommercial use is free, with copyright and license notices preserved.
- Private noncommercial use does not require publishing changes. When distributing a program or providing an online service using the library, publish the corresponding library source and all library modifications. Your entire application does not need to be published.
- Commercial use requires a separate written paid license, including company internal tools, commissioned work, and commercial research or development, even if the application is not sold or distributed.

For commercial licensing, contact [Cao (Starkxim)](https://github.com/Starkxim). Public source code does not grant free commercial permission. This custom license is not an OSI-approved open-source license; the full LICENSE text controls.

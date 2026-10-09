# Manual acceptance cases

Run on Windows with the .NET 10 Desktop Runtime. Build checks, headless parser execution, native comparison, and interactive GUI checks are separate evidence categories.

| Case | Preconditions and steps | Expected result / regression point |
| --- | --- | --- |
| Synthetic demo | Start viewer, choose Load demo | Seven rows: CAN1, CAN86, FD100, FD101, LIN11, LIN57, APP_TEXT65; time 0 through 0.006s; EXT for ID 0x42; correct byte sequences and text |
| Save and reopen | Save demo to a new path, close/restart viewer, open that file | Same fields and payloads after reopening; no state depends on the previous session |
| Unicode path | Save/reopen in a folder containing Chinese characters | Successful load with the same seven objects |
| Raw preservation | Open a producer log containing unsupported event types or header versions | Raw rows remain visible; unsupported timestamp is shown as a dash; unknown data is not reinterpreted as a supported message |
| Filter/details | Filter by 0x42, LIN, or a payload byte; select a row | Filtered preview and matching raw detail/text; clearing filter restores preview |
| Large file | Open a file with more than 10,000 objects | Actual count covers the whole file; first 10,000 retained; status states preview limit; filter does not imply a full-file search |
| Cancellation | Open a large file and choose Cancel; then load demo | Explicit incomplete status; demo succeeds; former file is released after background operation ends |
| Close while reading | Open a large file, close window | Cancellation is requested; no completed-scan claim; no callback accesses a disposed form |
| Wrong/truncated input | Open a non-BLF file, file truncated in a header/body, or invalid object signature | Explicit read failure, no successful EOF/100% claim |
| Container integrity | Open files with wrong zlib checksum, unknown compression, or length shorter/longer than declaration | Failure with an actionable integrity error; no fabricated zero payload or silently truncated container |
| Time resolutions | Use independently generated header1/header2 objects with raw 100000 and flag1; compare to flag2 raw1000000000 | Both normalize to 1000000000 ns; peeking twice does not advance |
| API lifecycle | Open with handle API; peek/skip/read; close twice | Correct count; first close=1, second=0; invalid handle operations return false/0; no out-of-span write |
| Resource cap | Open a declared object/container beyond configured limits | InvalidDataException before the corresponding oversized allocation |
| FD101 short data | Open a producer file where validBytes exceeds stored payload, and one with extension attributes | Short data warning with only actual bytes; extension variant stays raw |
| External compatibility | Open synthetic file in an independent BLF tool; compare objects to native reader locally if available | Report exactly the covered types/flags; do not infer every object type is supported |

The synthetic generator and reader alone do not prove third-party compatibility. No proprietary DLL is needed to use this project or run the GUI.

## Writer evidence

On 2026-10-09 local Linux stage 3 regression passed 121 tests per target
(58 reader/decoder + 63 writer), zero failures/skips and zero build warnings/errors.
Both net8.0 and net10.0 passed the bidirectional python-can 4.6.1 checks and
produced identical fixture bytes. [Actions run 37895947121](https://github.com/Starkxim/ManagedBlf/actions/runs/37895947121)
passed all seven jobs: Linux/Windows net8.0/net10.0 core regression (121 tests
each), two Linux external checks (Python 3.12.15/python-can 4.6.1), and the
Windows viewer Release build, packaging and window startup. Existing 58-test runs
below are historical reader/decoder acceptance, not evidence for the new writer.

Writer regression covers literal whole-file expected bytes, typed read-back,
empty files, multiple objects/containers, ID format flags, RTR/TX/DLC/payload,
channel/time boundaries, start/last metadata and overflow, buffer limits,
new-path/empty-stream restrictions, ownership/leaveOpen, repeated completion,
disposal and persistent I/O faults. The independent expected file is a
project-authored literal 224-byte hex value, not generated from writer logic.

The validation-only python-can 4.6.1 check reads two generated synthetic files
(empty and four frames in two uncompressed containers), verifies fields and
header accounting independently, and creates a control file read by ManagedBlf.
It covers CAN 1, explicit low-valued extended IDs, ID/channel boundaries, TX/RX,
RTR, DLC and payload. Tool timestamps use floating-point seconds with at most
1 microsecond absolute comparison tolerance; the byte test separately checks
nanoseconds. Tool UTC and zero-based channel conventions are accounted for
without assigning a BLF timezone. No captured logs or proprietary DLLs are used.
See [fixture provenance](../tests/ManagedBlf.Tests/README.md) and
[writer format/API policy](FORMAT.md#minimal-writer).

The existing v0.1.0-alpha downloads do not contain this new writer. Writer checks
do not complete Windows GUI interaction, macOS execution or compatibility with
all other BLF tools, compression modes and object types.

## Automated evidence

On 2026-10-09 the independent core suite passed 58 tests per target on Linux with
.NET 8.0.31 and 10.0.12. Run it with
`dotnet test tests/ManagedBlf.Tests/ManagedBlf.Tests.csproj -c Release`.
[Actions run 37874151520](https://github.com/Starkxim/ManagedBlf/actions/runs/37874151520)
passed all four Linux/Windows runtime jobs (58 tests each) and the separate
Windows viewer Release build, with zero build warnings/errors. No GUI mouse interaction or macOS regression is
claimed. The table above remains the manual acceptance checklist.

Alpha packaging checks use `scripts/package-viewer.ps1` on Windows: both deployment
variants include LICENSE and usage, generate a new synthetic demo, reject
overwrite, and create a viewer window. This smoke check is separate from the
interactive table. [Run 37874480695](https://github.com/Starkxim/ManagedBlf/actions/runs/37874480695)
passed both packages, generated 818-byte demos, rejected overwrites, and created
both viewer windows on Windows. [v0.1.0-alpha](https://github.com/Starkxim/ManagedBlf/releases/tag/v0.1.0-alpha) was published by
[release run 37877253845](https://github.com/Starkxim/ManagedBlf/actions/runs/37877253845) from source commit `8ec6a72`.
The release workflow reran Windows net8.0/net10.0 regression (58 tests each),
package content/demo/overwrite checks and both window startup checks.
On 2026-10-09 the two published ZIPs were independently downloaded: ZIP CRCs
and SHA256SUMS.txt matched; LICENSE text matched the tag (Windows CRLF differs
from repository LF); runtime configuration, excluded content and third-party notices were checked
against the exact official 10.0.12 NuGet packs. Both ZIPs include apphost notices;
the self-contained ZIP also includes .NET and Windows Desktop runtime notices.
Assets were refreshed by the explicit packaging correction tag
`v0.1.0-alpha-package.1`; application source and original tag are unchanged.
Framework-dependent ZIP: 153,179 bytes / 14 files, .NET 10 Desktop Runtime x64
required. Self-contained ZIP: 51,549,520 bytes / 285 files, .NET 10.0.12 included.
No GUI mouse interaction or complete external interoperability is claimed.

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

## Automated evidence

On 2026-10-09 the independent core suite passed 58 tests per target on Linux with
.NET 8.0.31 and 10.0.12. Run it with
`dotnet test tests/ManagedBlf.Tests/ManagedBlf.Tests.csproj -c Release`.
[Actions run 37874151520](https://github.com/Starkxim/ManagedBlf/actions/runs/37874151520)
passed all four Linux/Windows runtime jobs (58 tests each) and the separate
Windows viewer Release build, with zero build warnings/errors. No GUI mouse interaction or macOS regression is
claimed. The table above remains the manual acceptance checklist.

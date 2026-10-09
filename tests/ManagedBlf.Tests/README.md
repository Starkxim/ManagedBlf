# Independent synthetic regression fixtures

All bytes are authored for this repository from literal field offsets documented in
`docs/FORMAT.md`. No captured vehicle/business logs, native binaries, proprietary
source or third-party implementation are included. These synthetic fixtures may be
published under the repository LICENSE. They are assembled in memory; no binary
fixture is committed.

`Bytes.cs` does not reference production format constants, padding helpers, demo
code or a BLF writer. Zlib fixtures use a literal RFC1950 envelope and RFC1951 stored
block, with an independently calculated checksum. This tests an observed subset,
not complete external interoperability. Decoder expectations are literal IDs,
channels, flags and payloads; CAN86 metadata is deliberately distinct from data.

Run `dotnet test tests/ManagedBlf.Tests/ManagedBlf.Tests.csproj -c Release`.
Both net8.0 and net10.0 runtimes are required to execute both targets.

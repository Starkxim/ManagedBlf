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

Writer tests use literal typed inputs for round trips and a separately authored
224-byte hex expectation for the entire single-frame LOGG/container/CAN output.
The expected bytes are not produced by the writer, reader, `Bytes.cs`, demo or a
third-party tool. Offset/metadata assertions independently check empty,
multi-object and multi-container output. Synthetic stream implementations inject
I/O/seek/flush failures to test lifecycle behavior; no native DLL is involved.

`tests/ManagedBlf.Interop` is a separate validation-only console project. It uses
the public writer to emit synthetic fixtures from literal CAN fields, not to
create golden expected bytes. `scripts/check-writer-interop.py` declares its
expected messages/header fields independently and reads the output with pinned
python-can 4.6.1. It also writes its own synthetic control with python-can for
ManagedBlf to read. The external package is used under its own license and is not
a ManagedBlf library dependency. Generated files stay outside source control.
These checks cover only the documented CAN 1 subset, not full BLF interoperability.

Run `dotnet test tests/ManagedBlf.Tests/ManagedBlf.Tests.csproj -c Release`.
Both net8.0 and net10.0 runtimes are required to execute both targets.

To reproduce the external check, install Python 3.12 and use a fresh output directory:

```sh
python -m pip install -r scripts/writer-interop-requirements.txt
dotnet run --project tests/ManagedBlf.Interop -c Release -f net10.0 -- generate artifacts/writer-interop/net10.0
python scripts/check-writer-interop.py artifacts/writer-interop/net10.0
dotnet run --project tests/ManagedBlf.Interop -c Release -f net10.0 --no-build -- verify artifacts/writer-interop/net10.0/python-can-control.blf
```

Run from the repository root. CI repeats this check for net8.0 and net10.0 on
Linux. Existing paths are refused rather than overwritten; use another fresh
output directory for a repeated run.

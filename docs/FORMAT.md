# Supported format and API boundaries

This library reads an observed BLF subset and writes a smaller CAN subset; this document is not an official specification. Multi-byte fields are little endian unless explicitly identified as the big-endian zlib checksum.

## Stream structure

The LOGG file header declares the first top-level object offset. Header fields follow the author's binlog 5.5.6 observations; at least 80 bytes are required, and the usual full header is 144 bytes. Declared file size and object count are metadata, not proof of complete data. A restore point offset is honored only for API numbers at least 4010600 and must lie within the actual file, after its header.

LOG_CONTAINER objects use a 16-byte base header and a 16-byte container header. Compression 0 and 2 are supported; others raise an error. Declared and actual expanded length must agree. Container boundaries do not delimit inner objects: one object may span several containers. Direct top-level objects are also accepted. Zlib header, expanded length and Adler-32 are validated; detection of every kind of trailing compressed garbage is not guaranteed by the platform decompressor.

The legacy author-measured padding table is retained: types 6–10, 32, 33, 65, 69, 71, 72, 76–81, 83–85, 90, 92, 93, 96, 97, 102, and 117 have `objectSize % 4` padding bytes. Other types have no padding under this policy. Unknown/future producer padding rules are not guaranteed. Padding after the final object/container may be omitted. No signature scanning or silent damaged-stream recovery is performed.

Objects are limited to 16 MiB, containers to 64 MiB, and retained object-stream bytes to 80 MiB by default. These are reader policy limits, not format maxima. Container payload allocation and the returned object copy are additional bounded allocations; these limits are not a total process-memory quota. `BlfReaderOptions` can change them. A malformed reader stays faulted; construct a new reader after an error.

## Time

Header versions 1 and 2 have minimum sizes 32 and 40 respectively. Their flags at offset 16 identify 10 microsecond (1) or nanosecond (2) units; the primary timestamp is at offset 24. Overflow while normalizing is rejected. Unknown versions/resolutions remain raw and have no interpreted timestamp. Header 2's original hardware timestamp is not substituted for the primary timestamp. Version 3 is not interpreted in this release.

The measurement start/end fields are local wall-clock SYSTEMTIME values without a timezone. Invalid/unset dates return null from `ToDateTime`; relative timestamps remain available.

## Message subset

Offsets below are relative to `objectStart + headerSize`.

| Type | Supported body layout |
| --- | --- |
| CAN 1 / 86 | channel 0:u16, flags 2, DLC 3, ID 4:u32, data at 8; type 1 has 8 fixed bytes, type 86 has a variable data field followed by 8 metadata bytes |
| CAN FD 100 | channel 0:u16, flags 2, DLC 3, ID 4:u32, FD flags 13, valid byte count 14, 64 data bytes at 20; optional reserved tail is not required |
| CAN FD 101 | channel 0, DLC 1, valid byte count 2, ID 4:u32, FD flags 12:u32, direction 34, extension offset 35, variable payload at 40 |
| LIN 11 | channel 0:u16, ID 2, DLC 3, 8 data bytes at 4, direction 18 |
| LIN 57 | channel 12:u16, ID 37, DLC 38, data at 112, direction 122; basic body 132 bytes |
| APP_TEXT 65 | source 0:u32, reserved 4:u32, text length 8:u32, reserved 12:u32, serialized text at 16 |

The extended CAN ID flag is bit 31, not an ID-value threshold. FD flags differ between types 100 and 101. DLC is the raw code; `Data.Length` is the actual retained payload length. LIN directions 1 and 2 are both presented as transmission in the simple GUI; the raw object preserves the distinction.

CAN FD 101 with nonzero extension offset remains raw because available public descriptions disagree on offset interpretation. Short stored payloads are not invented or zero-filled: the decoded object includes a warning. No protocol meaning is assigned to unknown events, error objects, DBC signals, or native pointer-bearing structures.

APP_TEXT defaults to UTF-8 for the demo; this is an explicit presentation choice, not a file-wide encoding guarantee. Pass an `Encoding` to `TryDecode` for another producer. Raw bytes remain intact. Text is never treated as executable instructions, links to fetch, or code.

## Minimal writer

`BlfWriter` creates new files only. It requires an empty, writable and seekable
stream positioned at zero, used exclusively by the writer until successful completion. `Create(path, start)`
uses `FileMode.CreateNew` and refuses existing paths. It writes a 144-byte LOGG
header and compression-0 LOG_CONTAINERs containing complete CAN type 1 objects;
there is no append, zlib writing, indexing, recovery or native pointer ABI.

### Serialized fields and accounting

| Structure | Fields written |
| --- | --- |
| LOGG, 144 bytes | Size 144 at 4; file size/uncompressed size u64 at 16/24; object count u32 at 32; start/last SYSTEMTIME at 40/56; unknown API/application identity, compression level, restore offset and reserved fields zero |
| LOG_CONTAINER | Base header size 16/version 1/type 10; object size 32 + payload; compression method 0 at 16; expanded payload size u32 at 24; other container fields zero |
| CAN 1, 48 bytes | Header size 32/version 1/type 1; flags 2 at 16 (nanoseconds); client index/object version zero; timestamp u64 at 24; channel u16 at 32; TX bit 0/RTR bit 7 at 34; DLC at 35; identifier u32 at 36, extended marker bit 31; fixed 8-byte data storage at 40 |

For `N` CAN objects in `K` containers, both `FileSize` and
`UncompressedFileSize` are `144 + 48*N + 32*K`. Uncompressed accounting includes
file and container headers as well as expanded inner objects, following the fixed
public references below. `ObjectCount` is `N`, excluding container wrappers. Every
container payload is a multiple of 48, so the existing padding rule requires no
trailing bytes. An empty completed file has only the 144-byte header, count zero,
and identical start/last times. Unknown producer identity is zero; the writer
does not claim to be a particular Vector/binlog version.

`MaxContainerDataSize` defaults to 64 KiB and permits 48 through 64 MiB minus 32.
The allocated buffer/effective capacity rounds down to a multiple of 48; objects
are never split. Thus even the largest allowed whole container fits the reader's
default 64 MiB limit. `ObjectsWritten` counts accepted frames, including buffered
ones; it does not imply durable storage. Counts beyond the u32 field are rejected.

### Input and time policy

`BlfCanFrame` uses an explicit `IsExtended` flag, including extended IDs whose
numeric value fits 11 bits. Standard IDs are 0–0x7FF and extended IDs are
0–0x1FFFFFFF, supplied without the on-disk marker. Channel is one-based, 1–65535;
DLC is 0–8. Non-RTR data length must equal DLC exactly. RTR frames must have empty
logical data even with nonzero DLC. Unused bytes in the fixed storage field are
zero; these bytes are not added to the logical payload. `IsTransmit` sets TX bit 0.
Data is copied synchronously by `WriteCanMessage`; callers must not mutate it
concurrently with the call, and may reuse it afterward.

The required measurement start is a `DateTime` with `Kind.Unspecified`,
millisecond-aligned, in years 1601–9999. It is the caller's wall clock, without a
stored timezone. `TimestampNanoseconds` is a nonnegative signed `long`; values
that exceed the resulting wall-clock range are rejected. All relative nanoseconds
are serialized exactly. `LastObjectTime` is start plus the timestamp of the last
accepted frame, truncated to SYSTEMTIME milliseconds. Out-of-order timestamps
are allowed: this is the last-written time, not the maximum timestamp. The empty
file uses start for both fields. This deterministic wall-clock policy does not
reduce the precision of object timestamps.

### Ownership, completion and errors

Stream ownership transfers only when construction succeeds. On a constructor
failure, the caller retains its stream; `Create` closes the stream it opened.
Neither creation nor completion is transactional: an I/O failure can leave a
partial newly created file. A provisional header is not a completed recording.

`Complete` writes pending containers, backfills the file header, positions the
stream at the file end and flushes it. Repeating successful completion is a no-op
until disposal; writing after completion throws `InvalidOperationException`.
`Dispose` completes a healthy writer, then closes an owned stream even if
completion throws. Previously faulted writers are not finalized or retried;
disposal is idempotent. `leaveOpen: true` keeps the stream open, but the disposed
writer remains unusable (`ObjectDisposedException` from write/completion).

Invalid input raises `ArgumentException`/`ArgumentOutOfRangeException` before
accepting the frame and allows a corrected call. Object-count/address-space
limits are checked before acceptance. Stream write/seek/flush errors propagate
and permanently fault the writer; subsequent write/completion calls rethrow the
recorded failure. Flush is not a disk durability guarantee. No operations or
Dispose may run concurrently; the writer is single-consumer.

### Independent checks and limits

The regression suite compares an entire 224-byte, single-frame output to an
independently authored literal hex expectation, alongside typed round trips,
empty/multiple-object/multiple-container files and lifecycle/invalid-input/I/O
boundaries. Expected bytes are not generated by the production writer or reader.
The external check uses python-can **4.6.1** to read project-authored synthetic
empty and four-frame files, then checks a separate python-can-produced control
file with ManagedBlf. Coverage is limited to CAN 1, standard/extended IDs, RX/TX,
RTR, payload/DLC, channel boundaries and multiple uncompressed containers.
python-can is a validation-only dependency, not a core/library dependency.

python-can converts SYSTEMTIME as UTC and exposes channels as disk channel minus
one; these are tool conventions, not a BLF timezone guarantee. Its timestamps
are floating-point seconds, compared with an absolute tolerance of 1 microsecond;
literal-byte regression checks full relative nanosecond precision independently.
These checks do not establish compatibility with every BLF producer/object type
or replace GUI interaction acceptance. Local checks and cross-platform Actions
have passed. See the exact scope and results in
[manual/automated evidence](MANUAL-CHECKS.md#writer-evidence).

## Public references

- [Wireshark author-maintained BLF field definitions](https://www.wireshark.org/docs/wsar_html/blf_8h_source.html)
- [python-can author-maintained BLF module and compatibility observations](https://python-can.readthedocs.io/en/stable/_modules/can/io/blf.html)
- [Fixed python-can 4.6.1 BLF observations](https://python-can.readthedocs.io/en/v4.6.1/_modules/can/io/blf.html)
- [Fixed vector_blf file statistics](https://github.com/Technica-Engineering/vector_blf/blob/3512fc2ddca43248c95b773905d9c3ba46bc6570/src/Vector/BLF/FileStatistics.cpp)
- [Fixed vector_blf uncompressed accounting](https://github.com/Technica-Engineering/vector_blf/blob/3512fc2ddca43248c95b773905d9c3ba46bc6570/src/Vector/BLF/File.cpp)
- [Fixed vector_blf container fields](https://github.com/Technica-Engineering/vector_blf/blob/3512fc2ddca43248c95b773905d9c3ba46bc6570/src/Vector/BLF/LogContainer.cpp)
- [Fixed vector_blf CAN 1 field definitions](https://github.com/Technica-Engineering/vector_blf/blob/3512fc2ddca43248c95b773905d9c3ba46bc6570/src/Vector/BLF/CanMessage.h)
- [RFC 1950 zlib format](https://www.rfc-editor.org/rfc/rfc1950)
- [Observed restore-point limitations](https://github.com/Technica-Engineering/vector_blf/blob/master/src/Vector/BLF/RestorePointContainer.h)

These were consulted for field facts and interoperability limits. Third-party implementation source was not copied or linked into this project.

## Framework and regression scope

The core targets net8.0 and net10.0 with unchanged reader APIs and no external
package dependencies. The viewer targets net10.0-windows. Independent in-memory
fixtures in `tests/ManagedBlf.Tests` use literal offsets and their own stored-block
zlib envelope, without production padding/constants or demo generation. They
validate this subset and corruption/resource/lifecycle behavior, not every
compressed trailing-garbage variant or full external interoperability.

## Alpha distribution

[v0.1.0-alpha](https://github.com/Starkxim/ManagedBlf/releases/tag/v0.1.0-alpha) distributes only the Windows x64 viewer,
with framework-dependent (.NET 10 Desktop Runtime required) and self-contained
(.NET 10.0.12 included) ZIPs. Both include the custom LICENSE and synthetic demo
generation. Apphost/runtime license notices are provided under
`third-party-licenses` and retain their own terms. The exact source tag is `v0.1.0-alpha` at commit `8ec6a72`; no writer
is introduced by this release; the minimal writer described above is available in current source only. [Release validation](https://github.com/Starkxim/ManagedBlf/actions/runs/37877253845) and independent
published-ZIP checks establish package integrity and startup, not GUI interaction
or broader BLF interoperability.

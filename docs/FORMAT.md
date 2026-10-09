# Supported format and API boundaries

This is a reader for an observed BLF subset, not an official specification. Multi-byte fields are little endian unless explicitly identified as the big-endian zlib checksum.

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

## Public references

- [Wireshark author-maintained BLF field definitions](https://www.wireshark.org/docs/wsar_html/blf_8h_source.html)
- [python-can author-maintained BLF module and compatibility observations](https://python-can.readthedocs.io/en/stable/_modules/can/io/blf.html)
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
is introduced by this release. [Release validation](https://github.com/Starkxim/ManagedBlf/actions/runs/37877253845) and independent
published-ZIP checks establish package integrity and startup, not GUI interaction
or broader BLF interoperability.

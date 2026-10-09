"""Check public synthetic writer output using independent bytes and python-can.

No captured logs or third-party fixtures are used. python-can is an external test
tool, not a library dependency. Integer-millisecond inputs avoid treating its
floating-point Unix timestamps as proof of nanosecond accuracy. SYSTEMTIME carries
no time zone; UTC here is solely the comparison convention used by python-can.
"""

import argparse
from datetime import datetime, timezone
from importlib.metadata import version
from pathlib import Path
import struct
import sys

import can


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


# These expectations are declared here, independently of the .NET producer and
# ManagedBlf's constants/serialization. Channel values below are the BLF's 1-based
# values; python-can exposes them as 0-based indices.
FRAMES = [
    (0, 1, 0x123, False, False, False, 2, bytes.fromhex("12 34")),
    (1_000_000, 65535, 0x42, True, False, True, 8,
     bytes.fromhex("00 FF 10 20 30 40 50 60")),
    (2_000_000, 1, 0x7FF, False, True, False, 8, b""),
    (3_000_000, 65535, 0x1FFFFFFF, True, False, True, 0, b""),
]
START_SYSTEMTIME = (2024, 1, 2, 2, 3, 4, 5, 123)  # Tuesday = 2, Sunday = 0.
END_SYSTEMTIME = (2024, 1, 2, 2, 3, 4, 5, 126)
START_UNIX_SECONDS = datetime(2024, 1, 2, 3, 4, 5, 123000, tzinfo=timezone.utc).timestamp()


def check_bytes(path: Path, expected_frames: list, expected_containers: int) -> None:
    raw = path.read_bytes()
    require(len(raw) >= 144 and raw[:4] == b"LOGG", f"{path.name}: LOGG header")
    require(struct.unpack_from("<I", raw, 4)[0] == 144, f"{path.name}: header size")
    file_size, uncompressed_size, count = struct.unpack_from("<QQI", raw, 16)
    expected_size = 144 + 48 * len(expected_frames) + 32 * expected_containers
    require(file_size == len(raw) == expected_size, f"{path.name}: file size")
    require(uncompressed_size == expected_size, f"{path.name}: uncompressed size includes container headers")
    require(count == len(expected_frames), f"{path.name}: count excludes containers")
    require(raw[13] == 0, f"{path.name}: uncompressed file flag")
    require(struct.unpack_from("<8H", raw, 40) == START_SYSTEMTIME, f"{path.name}: measurement start")
    expected_end = END_SYSTEMTIME if expected_frames else START_SYSTEMTIME
    require(struct.unpack_from("<8H", raw, 56) == expected_end, f"{path.name}: last object wall clock")

    position = 144
    parsed = []
    containers = 0
    while position < len(raw):
        require(raw[position:position + 4] == b"LOBJ", f"{path.name}: container signature")
        header_size, header_version, object_size, object_type = struct.unpack_from("<HHII", raw, position + 4)
        require((header_size, header_version, object_type) == (16, 1, 10), f"{path.name}: container header")
        require(object_size >= 32 and position + object_size <= len(raw), f"{path.name}: container bounds")
        require(struct.unpack_from("<H", raw, position + 16)[0] == 0, f"{path.name}: compression 0")
        require(raw[position + 18:position + 24] == bytes(6), f"{path.name}: container reserved fields")
        require(raw[position + 28:position + 32] == bytes(4), f"{path.name}: container reserved tail")
        payload_size = struct.unpack_from("<I", raw, position + 24)[0]
        require(payload_size == 96 and object_size == 128, f"{path.name}: two complete frames per container")
        payload = raw[position + 32:position + object_size]
        for offset in range(0, payload_size, 48):
            obj = payload[offset:offset + 48]
            require(obj[:4] == b"LOBJ", f"{path.name}: CAN signature")
            require(struct.unpack_from("<HHIII", obj, 4) == (32, 1, 48, 1, 2), f"{path.name}: CAN header and nanosecond flag")
            require(obj[20:24] == bytes(4), f"{path.name}: object client/version fields")
            nanoseconds = struct.unpack_from("<Q", obj, 24)[0]
            channel, flags, dlc, identifier = struct.unpack_from("<HBBI", obj, 32)
            parsed.append((nanoseconds, channel, identifier & 0x1FFFFFFF,
                           bool(identifier & 0x80000000), bool(flags & 0x80),
                           bool(flags & 1), dlc, obj[40:40 + (0 if flags & 0x80 else dlc)]))
            require(flags & ~0x81 == 0, f"{path.name}: CAN flag bits")
            require(identifier & 0x60000000 == 0, f"{path.name}: identifier reserved bits")
            used = 0 if flags & 0x80 else dlc
            require(obj[40 + used:48] == bytes(8 - used), f"{path.name}: unused fixed storage bytes")
        position += object_size
        containers += 1
    require(position == len(raw) and containers == expected_containers, f"{path.name}: exact container count and end")
    require(parsed == expected_frames, f"{path.name}: independent byte expectations differ")


def check_python_reader(path: Path, expected_frames: list, start: float) -> None:
    with can.BLFReader(path) as reader:
        actual = list(reader)
    require(len(actual) == len(expected_frames), f"{path.name}: external frame count")
    for index, (message, expected) in enumerate(zip(actual, expected_frames)):
        nanoseconds, channel, identifier, extended, remote, transmit, dlc, data = expected
        fields = (message.channel, message.arbitration_id, message.is_extended_id,
                  message.is_remote_frame, not message.is_rx, message.dlc, bytes(message.data))
        require(fields == (channel - 1, identifier, extended, remote, transmit, dlc, data),
                f"{path.name}: python-can frame {index} fields")
        require(abs(message.timestamp - (start + nanoseconds / 1_000_000_000)) <= 0.000001,
                f"{path.name}: python-can frame {index} timestamp (1 microsecond tolerance)")


def make_external_control(path: Path) -> None:
    # This separate tool creates a public synthetic reference with a binary-exact
    # half-second delta. The .NET verify subcommand checks it against its own
    # literal expectations; it does not consume this Python expectation table.
    start = 1704164645.0
    with path.open("xb") as stream:
        with can.BLFWriter(stream, compression_level=0) as writer:
            writer.on_message_received(can.Message(
                timestamp=start, arbitration_id=0x321, is_extended_id=False,
                channel=0, is_rx=True, dlc=2, data=[0xDE, 0xAD], check=True))
            writer.on_message_received(can.Message(
                timestamp=start + 0.5, arbitration_id=0x55, is_extended_id=True,
                channel=65534, is_rx=False, is_remote_frame=True, dlc=8, check=True))
    check_python_reader(path, [
        (0, 1, 0x321, False, False, False, 2, bytes.fromhex("DE AD")),
        (500_000_000, 65535, 0x55, True, True, True, 8, b""),
    ], start)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Fresh directory populated by the .NET generate subcommand")
    args = parser.parse_args()
    require(version("python-can") == "4.6.1", "Install scripts/writer-interop-requirements.txt: python-can 4.6.1 required")
    empty = args.directory / "empty.blf"
    populated = args.directory / "flags-multicontainer.blf"
    check_bytes(empty, [], 0)
    check_bytes(populated, FRAMES, 2)
    check_python_reader(empty, [], START_UNIX_SECONDS)
    check_python_reader(populated, FRAMES, START_UNIX_SECONDS)
    make_external_control(args.directory / "python-can-control.blf")
    print(f"PASS: Python {sys.version.split()[0]}, python-can {version('python-can')}; "
          "2 ManagedBlf files, 4 CAN frames, 2 uncompressed containers, independent header/byte checks.")
    print("Generated python-can-control.blf with 2 independent frames; run the .NET verify subcommand.")
    print("Scope: synthetic classic CAN; external timestamps checked within 1 microsecond, not proof of full BLF compatibility.")


if __name__ == "__main__":
    main()

# Source provenance

ManagedBlf originated from two C# implementations, `BlfReader.cs` and `ManagedBinlog.cs`, based on the author's observations of BLF files and native binlog reading behavior. The library uses independent managed models, bounded buffer interfaces, and strict container validation.

The viewer, minimal CAN writer, synthetic fixture generators and independent regression expectations were written for this project. Running the library or viewer does not require a native binlog DLL, third-party application code, or business logs.

Public format documentation and compatibility observations are listed in [Format references](FORMAT.md#public-references). These sources were consulted for field facts; their implementation code is not included, translated, or a runtime dependency.

Writer field accounting was checked against pinned public vector_blf field
descriptions and python-can 4.6.1, linked in the format references. Only format
facts were consulted; no third-party implementation was copied or translated.
The external python-can package is used only by the interoperability validation
script under its own license, and is not linked into or distributed with the
ManagedBlf core. All generated interop inputs and literal byte expectations are
project-authored synthetic data, described in the
[test provenance](../tests/ManagedBlf.Tests/README.md); no binary logs are committed.

Vector and BLF names identify the format being read. The project is independent and is not endorsed by Vector Informatik.

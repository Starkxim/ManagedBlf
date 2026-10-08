# Source provenance

ManagedBlf originated from two C# implementations, `BlfReader.cs` and `ManagedBinlog.cs`, based on the author's observations of BLF files and native binlog reading behavior. The library uses independent managed models, bounded buffer interfaces, and strict container validation.

The viewer and synthetic fixture generator were written for this project. Running the library or viewer does not require a native binlog DLL, third-party application code, or business logs.

Public format documentation and compatibility observations are listed in [Format references](FORMAT.md#public-references). These sources were consulted for field facts; their implementation code is not included, translated, or a runtime dependency.

Vector and BLF names identify the format being read. The project is independent and is not endorsed by Vector Informatik.

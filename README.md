<div align="center">

# 🚀 GenericReader

**A generic, extensible binary reader for .NET**  
Effortlessly read from files, streams, buffers, or spans with a single unified API.

[![GitHub release](https://img.shields.io/github/v/release/NotOfficer/GenericReader?logo=github)](https://github.com/NotOfficer/GenericReader/releases/latest)
[![NuGet](https://img.shields.io/nuget/v/GenericReader?logo=nuget)](https://www.nuget.org/packages/GenericReader)
![NuGet Downloads](https://img.shields.io/nuget/dt/GenericReader?logo=nuget)
[![GitHub issues](https://img.shields.io/github/issues/NotOfficer/GenericReader?logo=github)](https://github.com/NotOfficer/GenericReader/issues)
[![License](https://img.shields.io/github/license/NotOfficer/GenericReader)](https://github.com/NotOfficer/GenericReader/blob/master/LICENSE)

</div>

---

## 📦 Installation

Install via [NuGet](https://www.nuget.org/packages/GenericReader):

```powershell
Install-Package GenericReader
```

---

## ✨ Features

- Read from **files**, **streams**, **buffers**, and **spans**
- Generic `Read<T>()` API for simplicity and flexibility
- Lightweight and easy to integrate

---

## 🔧 Example Usage

```csharp
using GenericReader;

// From file
using var fileReader = new GenericFileReader(@"C:\Test\Example.bin");
var numberFromFile = fileReader.Read<uint>();

// From stream
using var streamReader = new GenericStreamReader(GetStream());
var numberFromStream = streamReader.Read<uint>();

// From byte array
using var bufferReader = new GenericBufferReader(GetBuffer());
var numberFromBuffer = bufferReader.Read<uint>();

// From span
var spanReader = new GenericSpanReader(GetSpan());
var numberFromSpan = spanReader.Read<uint>();
```

---

## 🛡️ Corrupted Data

Every reader checks each read against the data before it reads or allocates anything, and all readers fail the same way:

| Exception | When |
|---|---|
| `EndOfStreamException` | The data ends before the read does, or the read starts past the end (even when it reads zero bytes) |
| `InvalidDataException` | The data contains a length that cannot be valid: a negative length prefix, an FString length whose byte count does not fit in an `int`, or a getter array larger than `MaxArrayAllocationSize` |
| `ArgumentOutOfRangeException` | An argument is invalid, such as a negative length, a position or seek target outside of `[0, max]`, or a negative `MaxArrayAllocationSize` |

A read that fails does not move `Position`, with two exceptions:

- A length prefix that was read before the failure stays read.
- Reads made of several steps keep the steps that succeeded. The overloads with an offset keep their seek, and an array of FStrings or of getter elements keeps the elements it read.

Positions past the end are allowed, as with streams; reading there fails. After `Dispose`, the buffer, file and stream readers throw `ObjectDisposedException` for every read and every access to the position; `Length` stays available. Readers over memory return `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>` views of it. If a file or stream turns out shorter than the `Length` it had when the reader was created, the read throws `EndOfStreamException`, and the destination may hold part of the data.

`ReadArray` with a getter allocates the array before it reads any element, and an element may take no bytes at all, so its length cannot be checked against the data. `MaxArrayAllocationSize` limits the array size in bytes instead. The default is `IGenericReader.DefaultMaxArrayAllocationSize`, 256 MiB:

```csharp
reader.MaxArrayAllocationSize = 64 * 1024 * 1024;
var entries = reader.ReadArray(() => new Entry(reader));
```

---

## 🤝 Contributing

Contributions are **welcome and appreciated**!

Whether it's fixing a typo, suggesting an improvement, or submitting a pull request — every bit helps.

---

## 📄 License

This project is licensed under the [MIT License](https://github.com/NotOfficer/GenericReader/blob/master/LICENSE).

---

<div align="center">

⭐️ Star the repo if you find it useful!  
Feel free to open an issue if you have any questions or feedback.

</div>

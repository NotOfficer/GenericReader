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

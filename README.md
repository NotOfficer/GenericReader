<div align="center">

# GenericReader

A generic binary reader for .NET

[![GitHub release](https://img.shields.io/github/v/release/NotOfficer/GenericReader?logo=github)](https://github.com/NotOfficer/GenericReader/releases/latest) [![Nuget](https://img.shields.io/nuget/v/GenericReader?logo=nuget)](https://www.nuget.org/packages/GenericReader) ![Nuget DLs](https://img.shields.io/nuget/dt/GenericReader?logo=nuget) [![GitHub issues](https://img.shields.io/github/issues/NotOfficer/GenericReader?logo=github)](https://github.com/NotOfficer/GenericReader/issues) [![GitHub License](https://img.shields.io/github/license/NotOfficer/GenericReader)](https://github.com/NotOfficer/GenericReader/blob/master/LICENSE)

</div>

## Example Usage

```cs
using GenericReader;

{
    using var reader = new GenericStreamReader(@"C:\Test\Example.bin");
    var testNum = reader.Read<uint>();
}

{
    var buffer = System.IO.File.ReadAllBytes(@"C:\Test\Example.bin");
    using var reader = new GenericBufferReader(buffer);
    var testNum = reader.Read<uint>();
}
```

### NuGet

```md
Install-Package GenericReader
```

### Contribute

If you can provide any help, may it only be spell checking please contribute!  
I am open for any contribution.

using System.Text;

namespace GenericReader;

// The characters of an FString, without its terminator, as a view of the reader's memory.
public readonly struct FStringMemory
{
    public ReadOnlyMemory<byte> Memory { get; }
    public bool IsUnicode { get; }

    public ReadOnlySpan<byte> GetSpan() => Memory.Span;
    public bool IsEmpty() => Memory.IsEmpty;
    public Encoding GetEncoding() => IsUnicode ? Encoding.Unicode : Encoding.UTF8;

    public FStringMemory(ReadOnlyMemory<byte> memory, bool isUnicode)
    {
        Memory = memory;
        IsUnicode = isUnicode;
    }

    public override string ToString() => GetEncoding().GetString(GetSpan());
}

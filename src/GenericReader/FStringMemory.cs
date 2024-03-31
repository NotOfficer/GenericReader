using System.Text;

namespace GenericReader;

public readonly struct FStringMemory
{
	public Memory<byte> Memory { get; }
	public bool IsUnicode { get; }
	public Span<byte> GetSpan() => Memory.Span;
	public bool IsEmpty() => Memory.IsEmpty;
	public Encoding GetEncoding() => IsUnicode ? Encoding.Unicode : Encoding.UTF8;

	public FStringMemory(Memory<byte> memory, bool isUnicode)
	{
		Memory = memory;
		IsUnicode = isUnicode;
	}

	public override string ToString() => GetEncoding().GetString(GetSpan());
}

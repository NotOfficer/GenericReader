using System;
using System.Text;

namespace GenericReader;

public readonly struct FStringMemory
{
	public Memory<byte> Memory { get; }
	public bool IsUnicode { get; }
	public Span<byte> Span => Memory.Span;
	public bool IsEmpty => Memory.IsEmpty;
	public Encoding Encoding => IsUnicode ? Encoding.Unicode : Encoding.UTF8;

	public FStringMemory(Memory<byte> memory, bool isUnicode)
	{
		Memory = memory;
		IsUnicode = isUnicode;
	}

	public override string ToString() => Encoding.GetString(Span);
}
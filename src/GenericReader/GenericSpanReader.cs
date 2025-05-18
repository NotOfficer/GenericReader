#if NET9_0_OR_GREATER
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace GenericReader;

public ref struct GenericSpanReader : IGenericReader
{
	public delegate T GetterFunc<out T>(ref GenericSpanReader reader);
	private const string ObsoleteMessage = "This method cannot be used since this reader is a ref struct";

	private readonly Span<byte> _span;

	public GenericSpanReader(byte[] buffer, int start, int length) : this(new Span<byte>(buffer, start, length)) { }
	public GenericSpanReader(byte[] buffer) : this(new Span<byte>(buffer)) { }
	public GenericSpanReader(ReadOnlyMemory<byte> memory) : this(memory.Span) { }
	public GenericSpanReader(Memory<byte> memory) : this(memory.Span) { }
	public GenericSpanReader(ReadOnlySpan<byte> span) : this(MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), span.Length)) { }
	public GenericSpanReader(Span<byte> span)
	{
		_span = span;
		LengthLong = Length = span.Length;
	}

	public int Position { get; set; }

	public long PositionLong
	{
		get => Position;
		set => Position = int.CreateChecked(value);
	}

	public TPosition GetPosition<TPosition>() where TPosition : IBinaryInteger<TPosition>
	{
		return TPosition.CreateChecked(Position);
	}

	public void SetPosition<TPosition>(TPosition position) where TPosition : IBinaryInteger<TPosition>
	{
		Position = int.CreateChecked(position);
	}

	public int Length { get; }
	public long LengthLong { get; }

	public TLength GetLength<TLength>() where TLength : IBinaryInteger<TLength>
	{
		return TLength.CreateChecked(Length);
	}

	public int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return Position;
	}

	public long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return Position;
	}

	public void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>
	{
		Position = origin switch
		{
			SeekOrigin.Begin => int.CreateChecked(offset),
			SeekOrigin.Current => Position + int.CreateChecked(offset),
			SeekOrigin.End => Length + int.CreateChecked(offset),
			_ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
		};
	}

	public T Read<T>() where T : struct
	{
		var result = Unsafe.ReadUnaligned<T>(ref _span[Position]);
		int size = Unsafe.SizeOf<T>();
		Position += size;
		return result;
	}

	public T Read<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where T : struct where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return Read<T>();
	}

	public void Read<T>(Span<T> dest) where T : struct
	{
		int size = Unsafe.SizeOf<T>();
		var span = MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref dest[0]), dest.Length * size);
		_span.Slice(Position, span.Length).CopyTo(span);
		Position += span.Length;
	}

	public void Read<T, TOffset>(Span<T> dest, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where T : struct where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		Read(dest);
	}

	public bool ReadBoolean() => Read<int>() != 0;
	public bool ReadBoolean<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadBoolean();
	}

	public string ReadString(Encoding enc)
	{
		int length = Read<int>();
		return ReadString(length, enc);
	}

	public string ReadString<TOffset>(Encoding enc, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadString(enc);
	}

	public string ReadString<TOffset>(int length, Encoding enc, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadString(length, enc);
	}

	public string ReadString(int length, Encoding enc)
	{
		string result = enc.GetString(_span.Slice(Position, length));
		Position += length;
		return result;
	}

	public string ReadFString()
	{
		// > 0 for ANSICHAR, < 0 for UCS2CHAR serialization
		int length = Read<int>();
		if (length == 0)
			return string.Empty;

		// 1 byte/char is removed because of null terminator ('\0')
		if (length < 0) // LoadUCS2Char, Unicode, 16-bit, fixed-width
		{
			// If length cannot be negated due to integer overflow, Ar is corrupted.
			if (length == int.MinValue)
				throw new ArgumentOutOfRangeException(nameof(length), "Archive is corrupted");

			int pLength = length * -sizeof(char);
			var span = _span.Slice(Position, pLength - sizeof(char));
			string result = Encoding.Unicode.GetString(span);
			Position += pLength;
			return result;
		}
		else
		{
			var span = _span.Slice(Position, length).TrimEnd(byte.MinValue);
			string result = Encoding.UTF8.GetString(span);
			Position += length;
			return result;
		}
	}

	public string ReadFString<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadFString();
	}

	public string[] ReadFStringArray()
	{
		int length = Read<int>();
		return ReadFStringArray(length);
	}

	public string[] ReadFStringArray<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadFStringArray();
	}

	public string[] ReadFStringArray(int length)
	{
		if (length == 0)
			return [];

		string[] result = GC.AllocateUninitializedArray<string>(length);

		for (int i = 0; i < length; i++)
			result[i] = ReadFString();

		return result;
	}

	public string[] ReadFStringArray<TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadFStringArray(length);
	}

	/*public FStringMemory ReadFStringMemory()
	{
		// > 0 for ANSICHAR, < 0 for UCS2CHAR serialization
		var length = Read<int>();
		if (length == 0)
			return default;

		// 1 byte/char is removed because of null terminator ('\0')
		if (length < 0) // LoadUCS2Char, Unicode, 16-bit, fixed-width
		{
			// If length cannot be negated due to integer overflow, Ar is corrupted.
			if (length == int.MinValue)
				throw new ArgumentOutOfRangeException(nameof(length), "Archive is corrupted");

			var pLength = length * -sizeof(char);
			var memory = _memory.Slice(Position, pLength - sizeof(char));
			Position += pLength;
			return new FStringMemory(memory, true);
		}
		else
		{
			var memory = _memory.Slice(Position, length - 1);
			Position += length;
			return new FStringMemory(memory, false);
		}
	}

	public FStringMemory[] ReadFStringMemoryArray(int length)
	{
		var result = new FStringMemory[length];

		for (var i = 0; i < length; i++)
			result[i] = ReadFStringMemory();

		return result;
	}

	public FStringMemory[] ReadFStringMemoryArray()
	{
		var length = Read<int>();
		return ReadFStringMemoryArray(length);
	}*/

	public T[] ReadArray<T>() where T : struct
	{
		int length = Read<int>();
		return ReadArray<T>(length);
	}

	public T[] ReadArray<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where T : struct
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadArray<T>();
	}

	public T[] ReadArray<T>(int length) where T : struct
	{
		if (length == 0)
			return [];

		int size = length * Unsafe.SizeOf<T>();
		var result = GC.AllocateUninitializedArray<T>(length);
		Unsafe.CopyBlockUnaligned(ref Unsafe.As<T, byte>(ref result[0]), ref _span[Position], (uint)size);
		Position += size;
		return result;
	}

	public T[] ReadArray<T, TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where T : struct
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadArray<T>(length);
	}

	public T[] ReadArray<T>(Func<T> getter)
	{
		int length = Read<int>();
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T, TOffset>(Func<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		int length = Read<int>();
		return ReadArray(length, getter);
	}
	
	[Obsolete(ObsoleteMessage, true)]
	public T[] ReadArray<T>(Func<IGenericReader, T> getter)
	{
		int length = Read<int>();
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T>(GetterFunc<T> getter)
	{
		int length = Read<int>();
		return ReadArray(length, getter);
	}
	
	[Obsolete(ObsoleteMessage, true)]
	public T[] ReadArray<T, TOffset>(Func<IGenericReader, T> getter, TOffset offset,
		SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		int length = Read<int>();
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T, TOffset>(GetterFunc<T> getter, TOffset offset,
		SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		int length = Read<int>();
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T>(int length, Func<T> getter)
	{
		if (length == 0)
			return [];

		var result = GC.AllocateUninitializedArray<T>(length);

		for (int i = 0; i < length; i++)
			result[i] = getter();

		return result;
	}

	public T[] ReadArray<T, TOffset>(int length, Func<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadArray(length, getter);
	}

	[Obsolete(ObsoleteMessage, true)]
	public T[] ReadArray<T>(int length, Func<IGenericReader, T> getter)
	{
		if (length == 0)
			return [];

		var result = GC.AllocateUninitializedArray<T>(length);

		/*for (var i = 0; i < length; i++)
			result[i] = getter(this);*/

		return result;
	}

	public T[] ReadArray<T>(int length, GetterFunc<T> getter)
	{
		if (length == 0)
			return [];

		var result = GC.AllocateUninitializedArray<T>(length);

		for (int i = 0; i < length; i++)
			result[i] = getter(ref this);

		return result;
	}
	
	[Obsolete(ObsoleteMessage, true)]
	public T[] ReadArray<T, TOffset>(int length, Func<IGenericReader, T> getter, TOffset offset,
		SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T, TOffset>(int length, GetterFunc<T> getter, TOffset offset,
		SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadArray(length, getter);
	}

	public GenericSpanReader Slice(int start, bool sliceAtPosition = false)
	{
		int sliceStart = sliceAtPosition ? start + Position : start;
		return new GenericSpanReader(_span.Slice(sliceStart));
	}

	public GenericSpanReader Slice(int start, int length, bool sliceAtPosition = false)
	{
		int sliceStart = sliceAtPosition ? start + Position : start;
		return new GenericSpanReader(_span.Slice(sliceStart, length));
	}

	public Span<byte> AsSpan(bool sliceAtPosition = false)
	{
		return sliceAtPosition ? _span.Slice(Position) : _span;
	}

	public Span<byte> ReadSpan()
	{
		int length = Read<int>();
		return ReadSpan(length);
	}

	public Span<byte> ReadSpan(int length)
	{
		var result = _span.Slice(Position, length);
		Position += length;
		return result;
	}

	public Span<T> ReadSpan<T>() where T : struct
	{
		int length = Read<int>();
		return ReadSpan<T>(length);
	}

	public Span<T> ReadSpan<T>(int length) where T : struct
	{
		int size = length * Unsafe.SizeOf<T>();
		var span = _span.Slice(Position, size);
		ref var reference = ref Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(span));
		var resultSpan = MemoryMarshal.CreateSpan(ref reference, length);
		Position += size;
		return resultSpan;
	}

	public void Dispose() { }
}
#endif

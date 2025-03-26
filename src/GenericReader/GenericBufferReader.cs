using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace GenericReader;

public class GenericBufferReader : GenericReaderBase
{
	private Memory<byte> _memory;

	public GenericBufferReader(byte[] buffer, int start, int length) : this(new Memory<byte>(buffer, start, length)) { }
	public GenericBufferReader(byte[] buffer) : this(new Memory<byte>(buffer)) { }
	public GenericBufferReader(ReadOnlyMemory<byte> memory) : this(MemoryMarshal.AsMemory(memory)) { }
	public GenericBufferReader(Memory<byte> memory)
	{
		_memory = memory;
		LengthLong = Length = memory.Length;
	}

	internal void SetBufferAndPosition(byte[] buffer, int position)
	{
		Position = position;
		_memory = buffer;
	}

	public override int Position { get; set; }

	public override long PositionLong
	{
		get => Position;
		set => Position = int.CreateChecked(value);
	}

	public override TPosition GetPosition<TPosition>()
	{
		return TPosition.CreateChecked(Position);
	}

	public override void SetPosition<TPosition>(TPosition position)
	{
		Position = int.CreateChecked(position);
	}

	public override int Length { get; }
	public override long LengthLong { get; }

	public override TLength GetLength<TLength>()
	{
		return TLength.CreateChecked(Length);
	}

	public override int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
	{
		SeekVoid(offset, origin);
		return Position;
	}

	public override long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
	{
		SeekVoid(offset, origin);
		return Position;
	}

	public override void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
	{
		Position = origin switch
		{
			SeekOrigin.Begin => int.CreateChecked(offset),
			SeekOrigin.Current => Position + int.CreateChecked(offset),
			SeekOrigin.End => Length + int.CreateChecked(offset),
			_ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
		};
	}

	public override T Read<T>() where T : struct
	{
		var result = Unsafe.ReadUnaligned<T>(ref _memory.Span[Position]);
		var size = Unsafe.SizeOf<T>();
		Position += size;
		return result;
	}

	public override void Read<T>(Span<T> dest) where T : struct
	{
		var size = Unsafe.SizeOf<T>();
		var span = MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref dest[0]), dest.Length * size);
		_memory.Span.Slice(Position, span.Length).CopyTo(span);
		Position += span.Length;
	}

	public override string ReadString(int length, Encoding enc)
	{
		return ReadString(length, enc, false);
	}

	internal string ReadString(int length, Encoding enc, bool trimNull)
	{
		var span = _memory.Span.Slice(Position, length);
		if (trimNull)
			span = span.TrimEnd(byte.MinValue);
		var result = enc.GetString(span);
		Position += length;
		return result;
	}

	public override string ReadFString()
	{
		// > 0 for ANSICHAR, < 0 for UCS2CHAR serialization
		var length = Read<int>();
		if (length == 0)
			return string.Empty;

		// 1 byte/char is removed because of null terminator ('\0')
		if (length < 0) // LoadUCS2Char, Unicode, 16-bit, fixed-width
		{
			// If length cannot be negated due to integer overflow, Ar is corrupted.
			if (length == int.MinValue)
				throw new ArgumentOutOfRangeException(nameof(length), "Archive is corrupted");

			var pLength = length * -sizeof(char);
			var span = _memory.Span.Slice(Position, pLength - sizeof(char));
			var result = Encoding.Unicode.GetString(span);
			Position += pLength;
			return result;
		}
		else
		{
			var span = _memory.Span.Slice(Position, length).TrimEnd(byte.MinValue);
			var result = Encoding.UTF8.GetString(span);
			Position += length;
			return result;
		}
	}

	public FStringMemory ReadFStringMemory()
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
			var memory = _memory.Slice(Position, length).TrimEnd(byte.MinValue);
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
	}

	public override T[] ReadArray<T>(int length) where T : struct
	{
		if (length == 0)
			return [];

		var size = length * Unsafe.SizeOf<T>();
		var result = new T[length];
		Unsafe.CopyBlockUnaligned(ref Unsafe.As<T, byte>(ref result[0]), ref _memory.Span[Position], (uint)size);
		Position += size;
		return result;
	}

	public GenericBufferReader Slice(int start, bool sliceAtPosition = false)
	{
		var sliceStart = sliceAtPosition ? start + Position : start;
		return new GenericBufferReader(_memory.Slice(sliceStart));
	}

	public GenericBufferReader Slice(int start, int length, bool sliceAtPosition = false)
	{
		var sliceStart = sliceAtPosition ? start + Position : start;
		return new GenericBufferReader(_memory.Slice(sliceStart, length));
	}

	public static GenericBufferReader LoadFromFile(SafeFileHandle handle)
	{
		var length = RandomAccess.GetLength(handle);
		var buffer = new byte[length];
		RandomAccess.Read(handle, buffer, 0);
		return new GenericBufferReader(buffer);
	}

	public static async ValueTask<GenericBufferReader> LoadFromFileAsync(SafeFileHandle handle,
		CancellationToken cancellationToken = default)
	{
		var length = RandomAccess.GetLength(handle);
		var buffer = new byte[length];
		var memory = new Memory<byte>(buffer);
		await RandomAccess.ReadAsync(handle, memory, 0, cancellationToken).ConfigureAwait(false);
		return new GenericBufferReader(memory);
	}

	public static GenericBufferReader LoadFromStream(Stream stream)
	{
		var buffer = new byte[stream.Length];
		stream.ReadExactly(buffer);
		return new GenericBufferReader(buffer);
	}

	public static async ValueTask<GenericBufferReader> LoadFromStreamAsync(Stream stream,
		CancellationToken cancellationToken = default)
	{
		var buffer = new byte[stream.Length];
		var memory = new Memory<byte>(buffer);
		await stream.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
		return new GenericBufferReader(memory);
	}

	public Memory<byte> AsMemory(bool sliceAtPosition = false)
	{
		return sliceAtPosition ? _memory.Slice(Position) : _memory;
	}

	public Span<byte> AsSpan(bool sliceAtPosition = false)
	{
		return sliceAtPosition ? _memory.Span.Slice(Position) : _memory.Span;
	}

	public Memory<byte> ReadMemory()
	{
		var length = Read<int>();
		return ReadMemory(length);
	}

	public Memory<byte> ReadMemory(int length)
	{
		var result = _memory.Slice(Position, length);
		Position += length;
		return result;
	}

	public Span<byte> ReadSpan()
	{
		var length = Read<int>();
		return ReadSpan(length);
	}

	public Span<byte> ReadSpan(int length)
	{
		var result = _memory.Span.Slice(Position, length);
		Position += length;
		return result;
	}

	public Span<T> ReadSpan<T>() where T : struct
	{
		var length = Read<int>();
		return ReadSpan<T>(length);
	}

	public Span<T> ReadSpan<T>(int length) where T : struct
	{
		var size = length * Unsafe.SizeOf<T>();
		var memorySpan = _memory.Span.Slice(Position, size);
		ref var reference = ref Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(memorySpan));
		var resultSpan = MemoryMarshal.CreateSpan(ref reference, length);
		Position += size;
		return resultSpan;
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing) { }
	}

	public override void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}

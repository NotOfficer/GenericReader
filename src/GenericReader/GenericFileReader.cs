using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace GenericReader;

public class GenericFileReader : GenericReaderBase
{
	private readonly SafeFileHandle _handle;

	public GenericFileReader(string filePath) : this(File.OpenHandle(filePath)) { }
	public GenericFileReader(SafeFileHandle handle)
	{
		_handle = handle;
		var fileLength = RandomAccess.GetLength(_handle);
		LengthLong = fileLength;
		Length = unchecked((int)fileLength);
	}

	public override int Position
	{
		get => int.CreateChecked(PositionLong);
		set => PositionLong = value;
	}

	public override long PositionLong { get; set; }

	public override TPosition GetPosition<TPosition>()
	{
		return TPosition.CreateChecked(PositionLong);
	}

	public override void SetPosition<TPosition>(TPosition position)
	{
		PositionLong = long.CreateChecked(position);
	}

	public override int Length { get; }
	public override long LengthLong { get; }

	public override TLength GetLength<TLength>()
	{
		return TLength.CreateChecked(LengthLong);
	}

	public override int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
	{
		return unchecked((int)SeekLong(offset, origin));
	}

	public override long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
	{
		SeekVoid(offset, origin);
		return PositionLong;
	}

	public override void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
	{
		PositionLong = origin switch
		{
			SeekOrigin.Begin => long.CreateChecked(offset),
			SeekOrigin.Current => PositionLong + long.CreateChecked(offset),
			SeekOrigin.End => LengthLong + long.CreateChecked(offset),
			_ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
		};
	}

	public override T Read<T>() where T : struct
	{
		var size = Unsafe.SizeOf<T>();
		T result;

		if (size > Constants.MaxStackSize)
		{
			var buffer = ArrayPool<byte>.Shared.Rent(size);
			var bytesRead = RandomAccess.Read(_handle, buffer, PositionLong);
			ThrowIfStreamEnd(bytesRead, size);
			result = Unsafe.ReadUnaligned<T>(ref buffer[0]);
			ArrayPool<byte>.Shared.Return(buffer);
		}
		else
		{
			Span<byte> span = stackalloc byte[size];
			var bytesRead = RandomAccess.Read(_handle, span, PositionLong);
			ThrowIfStreamEnd(bytesRead, size);
			result = Unsafe.ReadUnaligned<T>(ref span[0]);
		}

		PositionLong += size;
		return result;
	}

	public override void Read<T>(Span<T> dest)
	{
		if (dest.IsEmpty)
			return;

		var size = Unsafe.SizeOf<T>();
		var span = MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref dest[0]), dest.Length * size);
		var bytesRead = RandomAccess.Read(_handle, span, PositionLong);
		ThrowIfStreamEnd(bytesRead, span.Length);
		PositionLong += span.Length;
	}

	public override string ReadString(int length, Encoding enc)
	{
		string result;

		if (length > Constants.MaxStackSize)
		{
			var buffer = ArrayPool<byte>.Shared.Rent(length);
			var bytesRead = RandomAccess.Read(_handle, buffer, PositionLong);
			ThrowIfStreamEnd(bytesRead, length);
			result = enc.GetString(new ReadOnlySpan<byte>(buffer, 0, length));
			ArrayPool<byte>.Shared.Return(buffer);
		}
		else
		{
			Span<byte> span = stackalloc byte[length];
			var bytesRead = RandomAccess.Read(_handle, span, PositionLong);
			ThrowIfStreamEnd(bytesRead, length);
			result = enc.GetString(span);
		}

		PositionLong += length;
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
			var result = ReadString(pLength - sizeof(char), Encoding.Unicode);
			PositionLong += sizeof(char);
			return result;
		}
		else
		{
			var result = ReadString(length - 1, Encoding.UTF8);
			PositionLong += 1;
			return result;
		}
	}

	public override T[] ReadArray<T>(int length) where T : struct
	{
		if (length == 0)
			return [];

		var size = length * Unsafe.SizeOf<T>();
		var result = new T[length];

		if (size > Constants.MaxStackSize)
		{
			var buffer = ArrayPool<byte>.Shared.Rent(size);
			var bytesRead = RandomAccess.Read(_handle, buffer, PositionLong);
			ThrowIfStreamEnd(bytesRead, size);
			Unsafe.CopyBlockUnaligned(ref Unsafe.As<T, byte>(ref result[0]), ref buffer[0], (uint)size);
			ArrayPool<byte>.Shared.Return(buffer);
		}
		else
		{
			Span<byte> span = stackalloc byte[size];
			var bytesRead = RandomAccess.Read(_handle, span, PositionLong);
			ThrowIfStreamEnd(bytesRead, size);
			Unsafe.CopyBlockUnaligned(ref Unsafe.As<T, byte>(ref result[0]), ref span[0], (uint)size);
		}

		PositionLong += size;
		return result;
	}

	public byte[] ReadBytes(int length, bool useSharedArrayPool = false)
	{
		var buffer = useSharedArrayPool ? ArrayPool<byte>.Shared.Rent(length) : new byte[length];
		var bytesRead = RandomAccess.Read(_handle, new Span<byte>(buffer, 0, length), PositionLong);
		ThrowIfStreamEnd(bytesRead, length);
		PositionLong += length;
		return buffer;
	}

	public byte[] ReadBytes<TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current, bool useSharedArrayPool = false) 
		where TOffset : IBinaryInteger<TOffset>
	{
		Seek(offset, origin);
		return ReadBytes(length, useSharedArrayPool);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ThrowIfStreamEnd(int bytesRead, int shouldHaveRead)
	{
		if (bytesRead != shouldHaveRead)
			throw new EndOfStreamException($"Could not read {shouldHaveRead} bytes from file at position {PositionLong}");
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing) _handle?.Dispose();
	}

	public override void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}

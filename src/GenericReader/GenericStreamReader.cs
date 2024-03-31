using System;
using System.Buffers;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace GenericReader;

public class GenericStreamReader : GenericReaderBase
{
	private readonly Stream _stream;

	public GenericStreamReader(string filePath) : this(File.OpenRead(filePath)) { }
	public GenericStreamReader(Stream stream)
	{
		_stream = stream;
		var streamLength = _stream.Length;
		LengthLong = streamLength;
		Length = unchecked((int)streamLength);
	}

	public override int Position
	{
		get => unchecked((int)PositionLong);
		set => PositionLong = value;
	}

	public override long PositionLong
	{
		get => _stream.Position;
		set => _stream.Position = value;
	}

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
		return _stream.Seek(long.CreateChecked(offset), origin);
	}

	public override void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
	{
		_stream.Seek(long.CreateChecked(offset), origin);
	}

	public override T Read<T>() where T : struct
	{
		var size = Unsafe.SizeOf<T>();
		var buffer = ArrayPool<byte>.Shared.Rent(size);
		var bytesRead = _stream.Read(buffer, 0, Length);
		ThrowIfStreamEnd(bytesRead, size);
		var result = Unsafe.ReadUnaligned<T>(ref buffer[0]);
		ArrayPool<byte>.Shared.Return(buffer);
		return result;
	}

	public override string ReadString(int length, Encoding enc)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(length);
		var bytesRead = _stream.Read(buffer, 0, length);
		ThrowIfStreamEnd(bytesRead, length);
		var result = enc.GetString(new ReadOnlySpan<byte>(buffer, 0, length));
		ArrayPool<byte>.Shared.Return(buffer);
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
			var result = ReadString(length - 1 - sizeof(char), Encoding.UTF8);
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
		var buffer = ArrayPool<byte>.Shared.Rent(size);
		var bytesRead = _stream.Read(buffer, 0, size);
		ThrowIfStreamEnd(bytesRead, size);
		Unsafe.CopyBlockUnaligned(ref Unsafe.As<T, byte>(ref result[0]), ref buffer[0], (uint)size);
		ArrayPool<byte>.Shared.Return(buffer);
		return result;
	}

	public byte[] ReadBytes(int length, bool useSharedArrayPool = false)
	{
		var buffer = useSharedArrayPool ? ArrayPool<byte>.Shared.Rent(length) : new byte[length];
		var bytesRead = _stream.Read(buffer, 0, length);
		ThrowIfStreamEnd(bytesRead, length);
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
		if (disposing) _stream?.Dispose();
	}

	public override void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}
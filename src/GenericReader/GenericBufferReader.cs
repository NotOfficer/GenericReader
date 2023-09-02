﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Win32.SafeHandles;

namespace GenericReader;

public class GenericBufferReader : GenericReaderBase
{
	private readonly Memory<byte> _memory;

	public GenericBufferReader(byte[] buffer, int start, int length) : this(new Memory<byte>(buffer, start, length)) { }
	public GenericBufferReader(byte[] buffer) : this(new Memory<byte>(buffer)) { }
	public GenericBufferReader(ReadOnlyMemory<byte> memory) : this(MemoryMarshal.AsMemory(memory)) { }
	public GenericBufferReader(Memory<byte> memory)
	{
		_memory = memory;
		LengthLong = Length = memory.Length;
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

	public override string ReadString(int length, Encoding enc)
	{
		var result = enc.GetString(_memory.Span.Slice(Position, length));
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
			var span = _memory.Span.Slice(Position, length - 1);
			var result = Encoding.UTF8.GetString(span);
			Position += length;
			return result;
		}
	}

	public override T[] ReadArray<T>(int length) where T : struct
	{
		if (length == 0)
			return Array.Empty<T>();

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
		stream.Read(buffer);
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
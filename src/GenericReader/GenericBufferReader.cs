using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace GenericReader
{
	public sealed unsafe class GenericBufferReader : IGenericReader
	{
		private readonly byte[] _buffer;

		public GenericBufferReader(byte[] buffer)
		{
			_buffer = buffer;
			Size = buffer.Length;
		}

		public int Size { get; }

		private int _position;
		public long Position
		{
			get => _position;
			set => _position = (int)value;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Seek(int offset, SeekOrigin origin)
		{
			return _position = origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => offset + _position,
				SeekOrigin.End => _buffer.Length + offset,
				_ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
			};
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T Read<T>() where T : unmanaged
		{
			T result;

			fixed (byte* p = &_buffer[_position])
			{
				result = *(T*)p;
			}

			var size = sizeof(T);
			_position += size;
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T Read<T>(int offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged
		{
			Seek(offset, origin);
			return Read<T>();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool ReadBoolean()
		{
			return Read<int>() != 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool ReadBoolean(int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			return Read<int>(offset, origin) != 0;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte ReadByte()
		{
			return _buffer[_position++];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte ReadByte(int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			Seek(offset, origin);
			return _buffer[_position++];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte[] ReadBytes(int length)
		{
			if (length == 0)
			{
				return Array.Empty<byte>();
			}

			var result = new byte[length];

			fixed (byte* pResult = result)
			fixed (byte* pBuffer = &_buffer[_position])
			{
				Unsafe.CopyBlockUnaligned(pResult, pBuffer, (uint)length);
			}

			_position += length;
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte[] ReadBytes(int length, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			Seek(offset, origin);
			return ReadBytes(length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ReadString(Encoding enc)
		{
			var length = Read<int>();
			return ReadString(length, enc);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ReadString(Encoding enc, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			var length = Read<int>(offset, origin);
			return ReadString(length, enc);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ReadString(int length, Encoding enc)
		{
			var result = enc.GetString(_buffer, _position, length);
			_position += length;
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ReadString(int length, Encoding enc, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			Seek(offset, origin);
			return ReadString(length, enc);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ReadFString()
		{
			// > 0 for ANSICHAR, < 0 for UCS2CHAR serialization
			var length = Read<int>();

			if (length == 0)
			{
				return string.Empty;
			}

			// 1 byte/char is removed because of null terminator ('\0')
			if (length < 0) // LoadUCS2Char, Unicode, 16-bit, fixed-width
			{
				// If length cannot be negated due to integer overflow, Ar is corrupted.
				if (length == int.MinValue)
				{
					throw new ArgumentOutOfRangeException(nameof(length), "Archive is corrupted");
				}

				var pLength = -length * sizeof(char);
				var result = Encoding.Unicode.GetString(_buffer, _position, pLength - sizeof(char));
				_position += pLength;
				return result;
			}
			else
			{
				var result = Encoding.UTF8.GetString(_buffer, _position, length - 1);
				_position += length;
				return result;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ReadFString(int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			Seek(offset, origin);
			return ReadFString();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string[] ReadFStringArray()
		{
			var length = Read<int>();
			return ReadFStringArray(length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string[] ReadFStringArray(int offset, SeekOrigin origin)
		{
			var length = Read<int>(offset, origin);
			return ReadFStringArray(length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string[] ReadFStringArray(int length)
		{
			var result = new string[length];

			for (var i = 0; i < length; i++)
			{
				result[i] = ReadFString();
			}

			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string[] ReadFStringArray(int length, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			Seek(offset, origin);
			return ReadFStringArray(length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>() where T : unmanaged
		{
			var length = Read<int>();
			return ReadArray<T>(length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(int offset, SeekOrigin origin) where T : unmanaged
		{
			var length = Read<int>(offset, origin);
			return ReadArray<T>(length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(int length) where T : unmanaged
		{
			if (length == 0)
			{
				return Array.Empty<T>();
			}

			var size = length * sizeof(T);
			var result = new T[length];

			fixed (T* pResult = result)
			fixed (byte* pBuffer = &_buffer[_position])
			{
				Unsafe.CopyBlockUnaligned(pResult, pBuffer, (uint)size);
			}

			_position += size;
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(int length, int offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged
		{
			Seek(offset, origin);
			return ReadArray<T>(length);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(Func<T> getter)
		{
			var length = Read<int>();
			return ReadArray(length, getter);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(Func<T> getter, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			var length = Read<int>(offset, origin);
			return ReadArray(length, getter);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(Func<IGenericReader, T> getter)
		{
			var length = Read<int>();
			return ReadArray(length, getter);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(Func<IGenericReader, T> getter, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			var length = Read<int>(offset, origin);
			return ReadArray(length, getter);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(int length, Func<T> getter)
		{
			if (length == 0)
			{
				return Array.Empty<T>();
			}

			var result = new T[length];

			for (var i = 0; i < length; i++)
			{
				result[i] = getter();
			}

			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(int length, Func<T> getter, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			Seek(offset, origin);
			return ReadArray(length, getter);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(int length, Func<IGenericReader, T> getter)
		{
			if (length == 0)
			{
				return Array.Empty<T>();
			}

			var result = new T[length];

			for (var i = 0; i < length; i++)
			{
				result[i] = getter(this);
			}

			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(int length, Func<IGenericReader, T> getter, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			Seek(offset, origin);
			return ReadArray(length, getter);
		}

		public void Dispose() { }
	}
}
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace GenericReader
{
	public sealed unsafe class GenericStreamReader : IGenericReader
	{
		private readonly Stream _stream;

		public GenericStreamReader(string filePath) : this(File.OpenRead(filePath)) { }

		public GenericStreamReader(Stream stream)
		{
			_stream = stream;
			Size = (int)_stream.Length;
		}

		public int Size { get; }

		public long Position
		{
			get => _stream.Position;
			set => _stream.Position = value;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Seek(int offset, SeekOrigin origin)
		{
			return (int)_stream.Seek(offset, origin);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T Read<T>() where T : unmanaged
		{
			var size = sizeof(T);
			var buffer = ReadBytes(size);

			fixed (byte* p = buffer)
			{
				return *(T*)p;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T Read<T>(int offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged
		{
			var size = sizeof(T);
			var buffer = ReadBytes(size, offset, origin);

			fixed (byte* p = buffer)
			{
				return *(T*)p;
			}
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
			return ReadBytes(1)[0];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte ReadByte(int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			return ReadBytes(1, offset, origin)[0];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public byte[] ReadBytes(int length)
		{
			var result = new byte[length];
			_stream.Read(result);
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
			var buffer = ReadBytes(length);
			var result = enc.GetString(buffer);
			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public string ReadString(int length, Encoding enc, int offset, SeekOrigin origin = SeekOrigin.Current)
		{
			var buffer = ReadBytes(length, offset, origin);
			var result = enc.GetString(buffer);
			return result;
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
				var buffer = new byte[pLength];
				_stream.Read(buffer, 0, pLength);

				var result = Encoding.Unicode.GetString(buffer, 0, pLength - sizeof(char));
				return result;
			}
			else
			{
				var buffer = new byte[length];
				_stream.Read(buffer, 0, length);

				var result = Encoding.UTF8.GetString(buffer, 0, length - 1);
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
			var buffer = ReadBytes(size);

			var result = new T[length];

			fixed (T* pResult = result)
			fixed (byte* pBuffer = buffer)
			{
				Unsafe.CopyBlockUnaligned(pResult, pBuffer, (uint)size);
			}

			return result;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public T[] ReadArray<T>(int length, int offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged
		{
			if (length == 0)
			{
				return Array.Empty<T>();
			}

			var size = length * sizeof(T);
			var buffer = ReadBytes(size, offset, origin);

			var result = new T[length];

			fixed (T* pResult = result)
			fixed (byte* pBuffer = buffer)
			{
				Unsafe.CopyBlockUnaligned(pResult, pBuffer, (uint)size);
			}

			return result;
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

		public void Dispose()
		{
			_stream.Dispose();
		}
	}
}
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace GenericReader;

public class GenericFileReader : GenericReaderBase
{
    private const int DefaultBufferSize = 4096;

    private readonly SafeFileHandle _handle;
    private readonly GenericBufferReader _bufferReader;

    private byte[] _buffer;
    private long _bufferPosition;
    private long _bufferEndPosition;
    private int _bufferSize;
    private int _bufferSizeToAlloc;

    public GenericFileReader(string filePath, int bufferSize = DefaultBufferSize)
        : this(File.OpenHandle(filePath, options: FileOptions.RandomAccess), bufferSize) { }
    public GenericFileReader(SafeFileHandle handle, int bufferSize = DefaultBufferSize)
    {
        _buffer = [];
        _handle = handle;
        long fileLength = RandomAccess.GetLength(_handle);
        LengthLong = fileLength;
        Length = unchecked((int)fileLength);

        _bufferSizeToAlloc = bufferSize;
        _bufferPosition = _bufferEndPosition = -1;
        _bufferReader = new GenericBufferReader(Memory<byte>.Empty);
    }

    private void EnsureBufferAllocated(int size)
    {
        if (size > _bufferSize || PositionLong < _bufferPosition || PositionLong + size > _bufferEndPosition)
        {
            _bufferSizeToAlloc = Math.Max(_bufferSizeToAlloc, size);
            if (_bufferPosition != -1)
                ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = ArrayPool<byte>.Shared.Rent(_bufferSizeToAlloc);
            int bytesRead = RandomAccess.Read(_handle, _buffer, PositionLong);
            _bufferPosition = PositionLong;
            _bufferSize = bytesRead;
            _bufferEndPosition = PositionLong + bytesRead;
            _bufferReader.SetMemoryAndPosition(_buffer, (int)(PositionLong - _bufferPosition));
        }
        else
        {
            _bufferReader.Position = (int)(PositionLong - _bufferPosition);
        }
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
        int size = Unsafe.SizeOf<T>();
        EnsureBufferAllocated(size);
        T result = _bufferReader.Read<T>();
        PositionLong += size;
        return result;
    }

    public override void Read<T>(Span<T> dest) where T : struct
    {
        if (dest.IsEmpty)
            return;

        int size = Unsafe.SizeOf<T>() * dest.Length;
        EnsureBufferAllocated(size);
        _bufferReader.Read(dest);
        PositionLong += size;
    }

    public override string ReadString(int length, Encoding enc)
    {
        return ReadString(length, enc, false);
    }

    private string ReadString(int length, Encoding enc, bool trimNull)
    {
        EnsureBufferAllocated(length);
        string result = _bufferReader.ReadString(length, enc, trimNull);
        PositionLong += length;
        return result;
    }

    public override string ReadFString()
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
            string result = ReadString(pLength - sizeof(char), Encoding.Unicode, false);
            PositionLong += sizeof(char);
            return result;
        }
        else
        {
            string result = ReadString(length, Encoding.UTF8, true);
            return result;
        }
    }

    public override T[] ReadArray<T>(int length) where T : struct
    {
        if (length == 0)
            return [];

        int size = length * Unsafe.SizeOf<T>();
        EnsureBufferAllocated(size);
        T[] result = _bufferReader.ReadArray<T>(length);

        PositionLong += size;
        return result;
    }

    public byte[] ReadBytes(int length, bool useSharedArrayPool = false)
    {
        byte[] buffer = useSharedArrayPool
            ? ArrayPool<byte>.Shared.Rent(length)
            : GC.AllocateUninitializedArray<byte>(length);
        EnsureBufferAllocated(length);
        _bufferReader.Read(buffer.AsSpan(0, length));
        PositionLong += length;
        return buffer;
    }

    public byte[] ReadBytes<TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current, bool useSharedArrayPool = false) 
        where TOffset : IBinaryInteger<TOffset>
    {
        Seek(offset, origin);
        return ReadBytes(length, useSharedArrayPool);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_bufferPosition != -1)
                ArrayPool<byte>.Shared.Return(_buffer);
            _handle.Dispose();
        }
    }

    public override void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

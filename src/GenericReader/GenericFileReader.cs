using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace GenericReader;

// Reads a file through a cache of bufferSize bytes. Reads larger than the cache go straight to the file, so that
// a single large read neither grows the cache nor makes every later cache miss read that much.
public class GenericFileReader : GenericReaderBase
{
    private const int DefaultBufferSize = 4096;

    private readonly SafeFileHandle _handle;
    private readonly bool _leaveOpen;
    private readonly int _bufferSize;

    private byte[]? _buffer;    // rented from BufferPool on the first cache miss, returned by Dispose
    private long _bufferOffset; // the file offset of _buffer[0]
    private int _bufferLength;  // how many bytes of _buffer hold file data
    private long _position;

    // Where the cache comes from; the tests swap it for a pool that counts what is rented and returned.
    internal ArrayPool<byte> BufferPool { get; init; } = ArrayPool<byte>.Shared;

    public GenericFileReader(string filePath, int bufferSize = DefaultBufferSize)
        : this(OpenHandle(filePath, bufferSize), bufferSize) { }

    // Unless leaveOpen, the reader owns the handle: it disposes it, also when this constructor fails after the
    // arguments were validated.
    public GenericFileReader(SafeFileHandle handle, int bufferSize = DefaultBufferSize, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        _handle = handle;
        _leaveOpen = leaveOpen;
        _bufferSize = bufferSize;

        try
        {
            LengthLong = RandomAccess.GetLength(handle);
        }
        catch
        {
            if (!leaveOpen)
                handle.Dispose();
            throw;
        }
    }

    // validates bufferSize first, so that an invalid one does not leave an open handle behind
    private static SafeFileHandle OpenHandle(string filePath, int bufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);
        return File.OpenHandle(filePath, options: FileOptions.RandomAccess);
    }

    public override int Position
    {
        get => int.CreateChecked(PositionLong);
        set => PositionLong = value;
    }

    public override long PositionLong
    {
        get
        {
            ThrowIfDisposed();
            return _position;
        }
        set
        {
            ThrowIfDisposed();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override TPosition GetPosition<TPosition>()
    {
        return TPosition.CreateChecked(PositionLong);
    }

    public override void SetPosition<TPosition>(TPosition position)
    {
        ThrowIfDisposed();
        _position = ReaderHelpers.ToPosition(position, long.MaxValue);
    }

    public override int Length => int.CreateChecked(LengthLong);
    public override long LengthLong { get; }

    public override TLength GetLength<TLength>()
    {
        return TLength.CreateChecked(LengthLong);
    }

    public override int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        // checked against int.MaxValue before Position moves
        ThrowIfDisposed();
        _position = ReaderHelpers.GetSeekTarget(offset, origin, _position, LengthLong, int.MaxValue);
        return (int)_position;
    }

    public override long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        SeekVoid(offset, origin);
        return _position;
    }

    public override void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        ThrowIfDisposed();
        _position = ReaderHelpers.GetSeekTarget(offset, origin, _position, LengthLong, long.MaxValue);
    }

    // Throws unless byteCount (not negative) bytes are left from Position. Position is never negative, so the
    // subtraction cannot overflow; it is negative when Position lies past the end.
    private void EnsureAvailable(long byteCount)
    {
        ThrowIfDisposed();
        if (byteCount > LengthLong - _position)
            ThrowHelper.ThrowEndOfStream();
    }

    // The next byteCount bytes (not negative, at most _bufferSize) from the cache, without moving Position. After
    // Dispose the cache is empty, so that every read misses it and FillBuffer throws.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> PeekBuffered(int byteCount)
    {
        // Unsigned, so that this cannot overflow: both offsets are never negative, and a Position before the cache
        // turns into a huge offset.
        ulong offset = (ulong)(_position - _bufferOffset);
        if (offset > (ulong)_bufferLength || (ulong)byteCount > (ulong)_bufferLength - offset)
        {
            FillBuffer(byteCount);
            offset = 0;
        }

        return _buffer.AsSpan((int)offset, byteCount);
    }

    // Fills the cache from Position on, with at least byteCount bytes.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FillBuffer(int byteCount)
    {
        EnsureAvailable(byteCount);

        _buffer ??= BufferPool.Rent(_bufferSize);
        // emptied first: if the read throws, the cache holds nothing instead of stale data
        _bufferLength = 0;
        _bufferOffset = _position;
        // not past Length, so that a file that grew after it was opened cannot serve reads beyond it from the cache
        int fillLength = (int)Math.Min(_buffer.Length, LengthLong - _position);
        _bufferLength = ReadAtLeast(_buffer.AsSpan(0, fillLength), _position, byteCount);

        if (_bufferLength < byteCount)
            ThrowHelper.ThrowEndOfStream(); // the file got shorter after it was opened
    }

    // Reads dest.Length bytes from Position on, past the cache. The callers have checked that they are available.
    private void ReadDirect(Span<byte> dest)
    {
        if (ReadAtLeast(dest, _position, dest.Length) < dest.Length)
            ThrowHelper.ThrowEndOfStream(); // the file got shorter after it was opened
    }

    // RandomAccess.Read may return fewer bytes than requested, so this reads until minimumBytes or the end of the file.
    private int ReadAtLeast(Span<byte> buffer, long fileOffset, int minimumBytes)
    {
        int totalRead = 0;
        while (totalRead < minimumBytes)
        {
            int bytesRead = RandomAccess.Read(_handle, buffer[totalRead..], fileOffset + totalRead);
            if (bytesRead == 0)
                break;
            totalRead += bytesRead;
        }

        return totalRead;
    }

    // Reads dest.Length bytes and moves Position past them. The callers have checked that they are available.
    private void ReadBytesTo(Span<byte> dest)
    {
        if (dest.Length <= _bufferSize)
            PeekBuffered(dest.Length).CopyTo(dest);
        else
            ReadDirect(dest);

        _position += dest.Length;
    }

    public override T Read<T>()
    {
        int size = Unsafe.SizeOf<T>();
        T result;

        if (size <= _bufferSize)
        {
            result = MemoryMarshal.Read<T>(PeekBuffered(size));
        }
        else
        {
            EnsureAvailable(size);
            Unsafe.SkipInit(out result);
            ReadDirect(MemoryMarshal.AsBytes(new Span<T>(ref result)));
        }

        _position += size;
        return result;
    }

    public override void Read<T>(Span<T> dest)
    {
        // checked as a whole, so that a destination read in parts cannot fail half-way for lack of data
        EnsureAvailable((long)dest.Length * Unsafe.SizeOf<T>());
        ReadElements(dest);
    }

    // Fills dest and moves Position past it. The callers have checked that the bytes are available.
    private void ReadElements<T>(Span<T> dest) where T : unmanaged
    {
        // MemoryMarshal.AsBytes covers at most int.MaxValue bytes, so a larger destination is read in parts
        int chunkLength = int.MaxValue / Unsafe.SizeOf<T>();
        while (dest.Length > chunkLength)
        {
            ReadBytesTo(MemoryMarshal.AsBytes(dest[..chunkLength]));
            dest = dest[chunkLength..];
        }

        ReadBytesTo(MemoryMarshal.AsBytes(dest));
    }

    public override string ReadString(int length, Encoding enc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(enc);
        string result;

        if (length <= _bufferSize)
        {
            result = enc.GetString(PeekBuffered(length));
        }
        else
        {
            EnsureAvailable(length); // before renting
            using TempBuffer buffer = new(length, default);
            ReadDirect(buffer.Span);
            result = enc.GetString(buffer.Span);
        }

        _position += length;
        return result;
    }

    public override string ReadFString()
    {
        int length = Read<int>();
        if (length == 0)
            return string.Empty;

        // the terminator is read as well, so that a missing one throws
        int byteCount = ReaderHelpers.GetFStringByteCount(length);
        string result;

        if (byteCount <= _bufferSize)
        {
            result = ReaderHelpers.DecodeFString(PeekBuffered(byteCount), length);
        }
        else
        {
            EnsureAvailable(byteCount); // before renting
            using TempBuffer buffer = new(byteCount, default);
            ReadDirect(buffer.Span);
            result = ReaderHelpers.DecodeFString(buffer.Span, length);
        }

        _position += byteCount;
        return result;
    }

    public override T[] ReadArray<T>(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // 64-bit, so that the byte count cannot wrap around, and checked before allocating
        EnsureAvailable((long)length * Unsafe.SizeOf<T>());
        if (length == 0)
            return [];
        ReaderHelpers.ValidateArrayLength(length);

        T[] result = GC.AllocateUninitializedArray<T>(length);
        ReadElements<T>(result);
        return result;
    }

    // With useSharedArrayPool, the array comes from ArrayPool<byte>.Shared and may be longer than length; only the
    // first length bytes hold data.
    public byte[] ReadBytes(int length, bool useSharedArrayPool = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        EnsureAvailable(length);

        if (!useSharedArrayPool)
        {
            byte[] result = GC.AllocateUninitializedArray<byte>(length);
            ReadBytesTo(result);
            return result;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            ReadBytesTo(buffer.AsSpan(0, length));
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        return buffer;
    }

    public byte[] ReadBytes<TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current, bool useSharedArrayPool = false)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadBytes(length, useSharedArrayPool);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The cache is emptied before its array goes back to the pool, so that no read can see it afterwards.
            // An offset of -1 makes even zero-byte reads miss it, which then throw ObjectDisposedException.
            _bufferLength = 0;
            _bufferOffset = -1;
            if (_buffer is not null)
            {
                BufferPool.Return(_buffer);
                _buffer = null;
            }

            if (!_leaveOpen)
                _handle.Dispose();
        }
    }
}

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace GenericReader;

// Reads from a seekable stream. Every read is checked against Length before anything is allocated or read, and
// a read that fails puts the stream back where it started, even when the stream ends before the Length it reported
// (the destination may then hold part of the data).
public class GenericStreamReader : GenericReaderBase
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;

    public GenericStreamReader(string filePath) : this(File.OpenRead(filePath)) { }

    // Unless leaveOpen, the reader owns the stream: it disposes it, also when this constructor fails after the
    // argument was validated (for example for a stream that cannot seek).
    public GenericStreamReader(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _leaveOpen = leaveOpen;

        try
        {
            LengthLong = stream.Length;
        }
        catch
        {
            if (!leaveOpen)
                stream.Dispose();
            throw;
        }
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
            return _stream.Position;
        }
        set
        {
            ThrowIfDisposed();
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _stream.Position = value;
        }
    }

    public override TPosition GetPosition<TPosition>()
    {
        return TPosition.CreateChecked(PositionLong);
    }

    public override void SetPosition<TPosition>(TPosition position)
    {
        ThrowIfDisposed();
        _stream.Position = ReaderHelpers.ToPosition(position, long.MaxValue);
    }

    public override int Length => int.CreateChecked(LengthLong);
    public override long LengthLong { get; }

    public override TLength GetLength<TLength>()
    {
        return TLength.CreateChecked(LengthLong);
    }

    public override int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        // checked against int.MaxValue before the stream moves
        long target = ReaderHelpers.GetSeekTarget(offset, origin, PositionLong, LengthLong, int.MaxValue);
        _stream.Position = target;
        return (int)target;
    }

    public override long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        SeekVoid(offset, origin);
        return PositionLong;
    }

    public override void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        _stream.Position = ReaderHelpers.GetSeekTarget(offset, origin, PositionLong, LengthLong, long.MaxValue);
    }

    // Throws unless byteCount (not negative) bytes are left from Position, which it returns.
    private long EnsureAvailable(long byteCount)
    {
        long position = PositionLong;
        if (byteCount > LengthLong - position)
            ThrowHelper.ThrowEndOfStream();
        return position;
    }

    // Fills dest, or throws with the stream back at start, where dest begins. The callers have checked that the bytes
    // are available and pass the position that EnsureAvailable returned, so that it is not queried again.
    private void ReadExactly(Span<byte> dest, long start)
    {
        try
        {
            _stream.ReadExactly(dest);
        }
        catch
        {
            _stream.Position = start; // the stream ended before the Length it reported
            throw;
        }
    }

    // ReadExactly for elements, which may take more than int.MaxValue bytes.
    private void ReadExactly<T>(Span<T> dest, long start) where T : unmanaged
    {
        try
        {
            // MemoryMarshal.AsBytes covers at most int.MaxValue bytes, so a larger destination is read in parts
            int chunkLength = int.MaxValue / Unsafe.SizeOf<T>();
            while (dest.Length > chunkLength)
            {
                _stream.ReadExactly(MemoryMarshal.AsBytes(dest[..chunkLength]));
                dest = dest[chunkLength..];
            }

            _stream.ReadExactly(MemoryMarshal.AsBytes(dest));
        }
        catch
        {
            _stream.Position = start; // the stream ended before the Length it reported
            throw;
        }
    }

    public override T Read<T>()
    {
        long start = EnsureAvailable(Unsafe.SizeOf<T>());
        Unsafe.SkipInit(out T result);
        ReadExactly(MemoryMarshal.AsBytes(new Span<T>(ref result)), start);
        return result;
    }

    public override void Read<T>(Span<T> dest)
    {
        ReadExactly(dest, EnsureAvailable((long)dest.Length * Unsafe.SizeOf<T>()));
    }

    public override string ReadString(int length, Encoding enc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(enc);
        return ReadAndDecode(length, enc, fStringLength: 0);
    }

    public override string ReadFString()
    {
        int length = Read<int>();
        if (length == 0)
            return string.Empty;

        // the terminator is read as well, so that a missing one throws
        return ReadAndDecode(ReaderHelpers.GetFStringByteCount(length), Encoding.UTF8, length);
    }

    // Reads byteCount bytes and decodes them with enc, or as the FString of fStringLength when that is not 0. Throws
    // with the stream back where it started, also when decoding fails, like the other readers.
    private string ReadAndDecode(int byteCount, Encoding enc, int fStringLength)
    {
        long start = EnsureAvailable(byteCount);
        using TempBuffer buffer = new(byteCount, byteCount <= Constants.MaxStackSize ? stackalloc byte[byteCount] : default);
        try
        {
            _stream.ReadExactly(buffer.Span);
            return fStringLength == 0
                ? enc.GetString(buffer.Span)
                : ReaderHelpers.DecodeFString(buffer.Span, fStringLength);
        }
        catch
        {
            _stream.Position = start;
            throw;
        }
    }

    public override T[] ReadArray<T>(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // 64-bit, so that the byte count cannot wrap around, and checked before allocating
        long start = EnsureAvailable((long)length * Unsafe.SizeOf<T>());
        if (length == 0)
            return [];
        ReaderHelpers.ValidateArrayLength(length);

        T[] result = GC.AllocateUninitializedArray<T>(length);
        ReadExactly<T>(result, start);
        return result;
    }

    // With useSharedArrayPool, the array comes from ArrayPool<byte>.Shared and may be longer than length; only the
    // first length bytes hold data.
    public byte[] ReadBytes(int length, bool useSharedArrayPool = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        long start = EnsureAvailable(length);

        if (!useSharedArrayPool)
        {
            byte[] result = GC.AllocateUninitializedArray<byte>(length);
            ReadExactly(result, start);
            return result;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            ReadExactly(buffer.AsSpan(0, length), start);
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
        if (disposing && !_leaveOpen)
            _stream.Dispose();
    }
}

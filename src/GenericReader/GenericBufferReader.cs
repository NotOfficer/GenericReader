using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace GenericReader;

public class GenericBufferReader : GenericReaderBase
{
    private readonly ReadOnlyMemory<byte> _memory;
    private int _position;

    public GenericBufferReader(byte[] buffer, int start, int length) : this(new ReadOnlyMemory<byte>(buffer, start, length)) { }
    public GenericBufferReader(byte[] buffer) : this(new ReadOnlyMemory<byte>(buffer)) { }
    public GenericBufferReader(ReadOnlyMemory<byte> memory)
    {
        _memory = memory;
    }

    public override int Position
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

    public override long PositionLong
    {
        get => Position;
        set
        {
            ThrowIfDisposed();
            _position = ReaderHelpers.ToInt32Position(value);
        }
    }

    public override TPosition GetPosition<TPosition>()
    {
        return TPosition.CreateChecked(Position);
    }

    public override void SetPosition<TPosition>(TPosition position)
    {
        ThrowIfDisposed();
        _position = (int)ReaderHelpers.ToPosition(position, int.MaxValue);
    }

    public override int Length => _memory.Length;
    public override long LengthLong => _memory.Length;

    public override TLength GetLength<TLength>()
    {
        return TLength.CreateChecked(_memory.Length);
    }

    public override int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        SeekVoid(offset, origin);
        return _position;
    }

    public override long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        SeekVoid(offset, origin);
        return _position;
    }

    public override void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
    {
        ThrowIfDisposed();
        _position = (int)ReaderHelpers.GetSeekTarget(offset, origin, _position, _memory.Length, int.MaxValue);
    }

    // Throws unless byteCount (not negative) bytes are left from Position. Position is never negative, so the
    // subtraction cannot overflow; it is negative when Position lies past the end.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureAvailable(long byteCount)
    {
        ThrowIfDisposed();
        if (byteCount > _memory.Length - _position)
            ThrowHelper.ThrowEndOfStream();
    }

    // The next byteCount (not negative) bytes, without moving Position. The two Peek overloads are the only places
    // that create spans over the data, so no span is created without a bounds check.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> Peek(int byteCount)
    {
        ThrowIfDisposed();
        ReadOnlySpan<byte> span = _memory.Span;
        // both are at most int.MaxValue, so the unsigned sum cannot wrap around
        if ((uint)_position + (uint)byteCount > (uint)span.Length)
            ThrowHelper.ThrowEndOfStream();

        return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref MemoryMarshal.GetReference(span), (nuint)(uint)_position), byteCount);
    }

    // Peek for a computed byte count such as length * SizeOf<T>, which may not fit in an int.
    private ReadOnlySpan<byte> Peek(long byteCount)
    {
        EnsureAvailable(byteCount);
        // at most the length of the data now, so the cast cannot truncate
        return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref MemoryMarshal.GetReference(_memory.Span), (nuint)(uint)_position), (int)byteCount);
    }

    public override T Read<T>()
    {
        int size = Unsafe.SizeOf<T>();
        T result = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(Peek(size)));
        _position += size;
        return result;
    }

    public override void Read<T>(Span<T> dest)
    {
        // 64-bit: a destination of more than int.MaxValue bytes cannot fit in the data, but must not overflow either
        ReadOnlySpan<byte> source = Peek((long)dest.Length * Unsafe.SizeOf<T>());
        source.CopyTo(MemoryMarshal.AsBytes(dest));
        _position += source.Length;
    }

    public override string ReadString(int length, Encoding enc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(enc);
        string result = enc.GetString(Peek(length));
        _position += length;
        return result;
    }

    public override string ReadFString()
    {
        int length = Read<int>();
        if (length == 0)
            return string.Empty;

        int byteCount = ReaderHelpers.GetFStringByteCount(length);
        string result = ReaderHelpers.DecodeFString(Peek(byteCount), length);
        _position += byteCount;
        return result;
    }

    public FStringMemory ReadFStringMemory()
    {
        int length = Read<int>();
        if (length == 0)
            return default;

        int byteCount = ReaderHelpers.GetFStringByteCount(length);
        EnsureAvailable(byteCount);
        ReadOnlyMemory<byte> memory = _memory.Slice(_position, byteCount);
        _position += byteCount;

        // without the terminator
        return length < 0
            ? new FStringMemory(memory[..^sizeof(char)], true)
            : new FStringMemory(memory.TrimEnd(byte.MinValue), false);
    }

    public FStringMemory[] ReadFStringMemoryArray(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // every FString takes at least 4 bytes (its length), so a larger count is rejected before allocating
        EnsureAvailable((long)length * sizeof(int));
        if (length == 0)
            return [];

        FStringMemory[] result = new FStringMemory[length];

        for (int i = 0; i < length; i++)
            result[i] = ReadFStringMemory();

        return result;
    }

    public FStringMemory[] ReadFStringMemoryArray() => ReadFStringMemoryArray(ReadLength());

    public override T[] ReadArray<T>(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // 64-bit, so that the byte count cannot wrap around, and checked before allocating
        ReadOnlySpan<byte> source = Peek((long)length * Unsafe.SizeOf<T>());
        if (length == 0)
            return [];

        T[] result = GC.AllocateUninitializedArray<T>(length);
        source.CopyTo(MemoryMarshal.AsBytes(result.AsSpan()));
        _position += source.Length;
        return result;
    }

    public GenericBufferReader Slice(int start, bool sliceAtPosition = false)
    {
        int sliceStart = sliceAtPosition ? start + _position : start;
        return new GenericBufferReader(_memory.Slice(sliceStart)) { MaxArrayAllocationSize = MaxArrayAllocationSize };
    }

    public GenericBufferReader Slice(int start, int length, bool sliceAtPosition = false)
    {
        int sliceStart = sliceAtPosition ? start + _position : start;
        return new GenericBufferReader(_memory.Slice(sliceStart, length)) { MaxArrayAllocationSize = MaxArrayAllocationSize };
    }

    public static GenericBufferReader LoadFromFile(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        byte[] buffer = ReaderHelpers.AllocateBuffer(RandomAccess.GetLength(handle));

        // RandomAccess.Read may return fewer bytes than requested
        for (int totalRead = 0; totalRead < buffer.Length;)
        {
            int bytesRead = RandomAccess.Read(handle, buffer.AsSpan(totalRead), totalRead);
            if (bytesRead == 0)
                ThrowHelper.ThrowEndOfStream(); // the file got shorter
            totalRead += bytesRead;
        }

        return new GenericBufferReader(buffer);
    }

    public static async ValueTask<GenericBufferReader> LoadFromFileAsync(SafeFileHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        byte[] buffer = ReaderHelpers.AllocateBuffer(RandomAccess.GetLength(handle));

        // RandomAccess.ReadAsync may return fewer bytes than requested
        for (int totalRead = 0; totalRead < buffer.Length;)
        {
            int bytesRead = await RandomAccess.ReadAsync(handle, buffer.AsMemory(totalRead), totalRead, cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
                ThrowHelper.ThrowEndOfStream(); // the file got shorter
            totalRead += bytesRead;
        }

        return new GenericBufferReader(buffer);
    }

    // Loads the rest of the stream, from its current position on.
    public static GenericBufferReader LoadFromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] buffer = ReaderHelpers.AllocateBuffer(stream.Length - stream.Position);
        stream.ReadExactly(buffer);
        return new GenericBufferReader(buffer);
    }

    // Loads the rest of the stream, from its current position on.
    public static async ValueTask<GenericBufferReader> LoadFromStreamAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] buffer = ReaderHelpers.AllocateBuffer(stream.Length - stream.Position);
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return new GenericBufferReader(buffer);
    }

    public ReadOnlyMemory<byte> AsMemory(bool sliceAtPosition = false)
    {
        return sliceAtPosition ? _memory.Slice(_position) : _memory;
    }

    public ReadOnlySpan<byte> AsSpan(bool sliceAtPosition = false)
    {
        return sliceAtPosition ? _memory.Span.Slice(_position) : _memory.Span;
    }

    public ReadOnlyMemory<byte> ReadMemory() => ReadMemory(ReadLength());

    public ReadOnlyMemory<byte> ReadMemory(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        EnsureAvailable(length);
        ReadOnlyMemory<byte> result = _memory.Slice(_position, length);
        _position += length;
        return result;
    }

    public ReadOnlySpan<byte> ReadSpan() => ReadSpan(ReadLength());

    public ReadOnlySpan<byte> ReadSpan(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ReadOnlySpan<byte> result = Peek(length);
        _position += length;
        return result;
    }

    public ReadOnlySpan<T> ReadSpan<T>() where T : unmanaged => ReadSpan<T>(ReadLength());

    public ReadOnlySpan<T> ReadSpan<T>(int length) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // 64-bit, so that a wrapped byte count cannot pass the check while the span keeps the full length
        ReadOnlySpan<byte> source = Peek((long)length * Unsafe.SizeOf<T>());
        _position += source.Length;
        return MemoryMarshal.Cast<byte, T>(source);
    }
}

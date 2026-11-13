using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace GenericReader;

public ref struct GenericSpanReader : IGenericReader
{
    public delegate T GetterFunc<out T>(ref GenericSpanReader reader);
    private const string ObsoleteMessage = "This method cannot be used since this reader is a ref struct";

    private readonly ReadOnlySpan<byte> _span;
    private int _position;
    // stored as the difference to the default, so that default(GenericSpanReader) has the default limit too
    private long _maxArrayAllocationSizeOffset;

    public GenericSpanReader(byte[] buffer, int start, int length) : this(new ReadOnlySpan<byte>(buffer, start, length)) { }
    public GenericSpanReader(byte[] buffer) : this(new ReadOnlySpan<byte>(buffer)) { }
    public GenericSpanReader(ReadOnlyMemory<byte> memory) : this(memory.Span) { }
    public GenericSpanReader(ReadOnlySpan<byte> span)
    {
        _span = span;
    }

    public long MaxArrayAllocationSize
    {
        readonly get => IGenericReader.DefaultMaxArrayAllocationSize + _maxArrayAllocationSizeOffset;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _maxArrayAllocationSizeOffset = value - IGenericReader.DefaultMaxArrayAllocationSize;
        }
    }

    public int Position
    {
        readonly get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public long PositionLong
    {
        readonly get => _position;
        set => _position = ReaderHelpers.ToInt32Position(value);
    }

    public readonly TPosition GetPosition<TPosition>() where TPosition : IBinaryInteger<TPosition>
    {
        return TPosition.CreateChecked(_position);
    }

    public void SetPosition<TPosition>(TPosition position) where TPosition : IBinaryInteger<TPosition>
    {
        _position = (int)ReaderHelpers.ToPosition(position, int.MaxValue);
    }

    public readonly int Length => _span.Length;
    public readonly long LengthLong => _span.Length;

    public readonly TLength GetLength<TLength>() where TLength : IBinaryInteger<TLength>
    {
        return TLength.CreateChecked(_span.Length);
    }

    public int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return _position;
    }

    public long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return _position;
    }

    public void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>
    {
        _position = (int)ReaderHelpers.GetSeekTarget(offset, origin, _position, _span.Length, int.MaxValue);
    }

    // Throws unless byteCount (not negative) bytes are left from Position. Position is never negative, so the
    // subtraction cannot overflow; it is negative when Position lies past the end.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void EnsureAvailable(long byteCount)
    {
        if (byteCount > _span.Length - _position)
            ThrowHelper.ThrowEndOfStream();
    }

    // The next byteCount (not negative) bytes, without moving Position. The two Peek overloads are the only places
    // that create spans over the data, so no span is created without a bounds check.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly ReadOnlySpan<byte> Peek(int byteCount)
    {
        // both are at most int.MaxValue, so the unsigned sum cannot wrap around
        if ((uint)_position + (uint)byteCount > (uint)_span.Length)
            ThrowHelper.ThrowEndOfStream();

        return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref MemoryMarshal.GetReference(_span), (nuint)(uint)_position), byteCount);
    }

    // Peek for a computed byte count such as length * SizeOf<T>, which may not fit in an int.
    private readonly ReadOnlySpan<byte> Peek(long byteCount)
    {
        EnsureAvailable(byteCount);
        // at most the length of the data now, so the cast cannot truncate
        return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref MemoryMarshal.GetReference(_span), (nuint)(uint)_position), (int)byteCount);
    }

    // A length prefix in the data: a negative one means the data is corrupted.
    private int ReadLength()
    {
        int length = Read<int>();
        if (length < 0)
            ThrowHelper.ThrowNegativeLength(length);
        return length;
    }

    public T Read<T>() where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        T result = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(Peek(size)));
        _position += size;
        return result;
    }

    public T Read<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where T : unmanaged where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return Read<T>();
    }

    public void Read<T>(Span<T> dest) where T : unmanaged
    {
        // 64-bit: a destination of more than int.MaxValue bytes cannot fit in the data, but must not overflow either
        ReadOnlySpan<byte> source = Peek((long)dest.Length * Unsafe.SizeOf<T>());
        source.CopyTo(MemoryMarshal.AsBytes(dest));
        _position += source.Length;
    }

    public void Read<T, TOffset>(Span<T> dest, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where T : unmanaged where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        Read(dest);
    }

    public bool ReadBoolean() => Read<int>() != 0;
    public bool ReadBoolean<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadBoolean();
    }

    public string ReadString(Encoding enc)
    {
        ArgumentNullException.ThrowIfNull(enc);
        return ReadString(ReadLength(), enc);
    }

    public string ReadString<TOffset>(Encoding enc, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadString(enc);
    }

    public string ReadString<TOffset>(int length, Encoding enc, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadString(length, enc);
    }

    public string ReadString(int length, Encoding enc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(enc);
        string result = enc.GetString(Peek(length));
        _position += length;
        return result;
    }

    public string ReadFString()
    {
        int length = Read<int>();
        if (length == 0)
            return string.Empty;

        int byteCount = ReaderHelpers.GetFStringByteCount(length);
        string result = ReaderHelpers.DecodeFString(Peek(byteCount), length);
        _position += byteCount;
        return result;
    }

    public string ReadFString<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadFString();
    }

    public string[] ReadFStringArray() => ReadFStringArray(ReadLength());

    public string[] ReadFStringArray<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadFStringArray();
    }

    public string[] ReadFStringArray(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // every FString takes at least 4 bytes (its length), so a larger count is rejected before allocating
        EnsureAvailable((long)length * sizeof(int));
        if (length == 0)
            return [];

        string[] result = new string[length];

        for (int i = 0; i < length; i++)
            result[i] = ReadFString();

        return result;
    }

    public string[] ReadFStringArray<TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadFStringArray(length);
    }

    public T[] ReadArray<T>() where T : unmanaged => ReadArray<T>(ReadLength());

    public T[] ReadArray<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where T : unmanaged
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadArray<T>();
    }

    public T[] ReadArray<T>(int length) where T : unmanaged
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

    public T[] ReadArray<T, TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where T : unmanaged
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadArray<T>(length);
    }

    public T[] ReadArray<T>(Func<T> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        return ReadArray(ReadLength(), getter);
    }

    public T[] ReadArray<T, TOffset>(Func<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadArray(getter);
    }

    public T[] ReadArray<T>(GetterFunc<T> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        return ReadArray(ReadLength(), getter);
    }

    public T[] ReadArray<T, TOffset>(GetterFunc<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadArray(getter);
    }

    public T[] ReadArray<T>(int length, Func<T> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ReaderHelpers.ValidateGetterArrayLength<T>(length, MaxArrayAllocationSize);
        if (length == 0)
        {
            EnsureAvailable(0);
            return [];
        }

        T[] result = GC.AllocateUninitializedArray<T>(length);

        for (int i = 0; i < length; i++)
            result[i] = getter();

        return result;
    }

    public T[] ReadArray<T, TOffset>(int length, Func<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadArray(length, getter);
    }

    public T[] ReadArray<T>(int length, GetterFunc<T> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ReaderHelpers.ValidateGetterArrayLength<T>(length, MaxArrayAllocationSize);
        if (length == 0)
        {
            EnsureAvailable(0);
            return [];
        }

        T[] result = GC.AllocateUninitializedArray<T>(length);

        for (int i = 0; i < length; i++)
            result[i] = getter(ref this);

        return result;
    }

    public T[] ReadArray<T, TOffset>(int length, GetterFunc<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadArray(length, getter);
    }

    // A ref struct cannot be passed as IGenericReader, so these getters cannot be called. They throw instead of being
    // left out because the interface requires them; [Obsolete] does not stop calls through a generic TReader.
    [Obsolete(ObsoleteMessage, true)]
    public readonly T[] ReadArray<T>(Func<IGenericReader, T> getter) => throw new NotSupportedException(ObsoleteMessage);

    [Obsolete(ObsoleteMessage, true)]
    public readonly T[] ReadArray<T, TOffset>(Func<IGenericReader, T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset> => throw new NotSupportedException(ObsoleteMessage);

    [Obsolete(ObsoleteMessage, true)]
    public readonly T[] ReadArray<T>(int length, Func<IGenericReader, T> getter) => throw new NotSupportedException(ObsoleteMessage);

    [Obsolete(ObsoleteMessage, true)]
    public readonly T[] ReadArray<T, TOffset>(int length, Func<IGenericReader, T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset> => throw new NotSupportedException(ObsoleteMessage);

    public readonly GenericSpanReader Slice(int start, bool sliceAtPosition = false)
    {
        int sliceStart = sliceAtPosition ? start + _position : start;
        return new GenericSpanReader(_span.Slice(sliceStart)) { _maxArrayAllocationSizeOffset = _maxArrayAllocationSizeOffset };
    }

    public readonly GenericSpanReader Slice(int start, int length, bool sliceAtPosition = false)
    {
        int sliceStart = sliceAtPosition ? start + _position : start;
        return new GenericSpanReader(_span.Slice(sliceStart, length)) { _maxArrayAllocationSizeOffset = _maxArrayAllocationSizeOffset };
    }

    public readonly ReadOnlySpan<byte> AsSpan(bool sliceAtPosition = false)
    {
        return sliceAtPosition ? _span.Slice(_position) : _span;
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

    public readonly void Dispose() { }
}

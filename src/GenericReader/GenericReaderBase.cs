using System.Numerics;
using System.Text;

namespace GenericReader;

// After Dispose, every read and every access to the position (Position, Seek, SetPosition) throws
// ObjectDisposedException; Length stays available.
public abstract class GenericReaderBase : IGenericReader
{
    private bool _disposed;

    private protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public long MaxArrayAllocationSize
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = IGenericReader.DefaultMaxArrayAllocationSize;

    public abstract int Position { get; set; }
    public abstract long PositionLong { get; set; }
    public abstract TPosition GetPosition<TPosition>() where TPosition : IBinaryInteger<TPosition>;
    public abstract void SetPosition<TPosition>(TPosition position) where TPosition : IBinaryInteger<TPosition>;
    public abstract int Length { get; }
    public abstract long LengthLong { get; }
    public abstract TLength GetLength<TLength>() where TLength : IBinaryInteger<TLength>;
    public abstract int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    public abstract long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    public abstract void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;

    // A length prefix in the data: a negative one means the data is corrupted.
    private protected int ReadLength()
    {
        int length = Read<int>();
        if (length < 0)
            ThrowHelper.ThrowNegativeLength(length);
        return length;
    }

    // Reading at a position past the end fails, even when no bytes are read.
    private protected void EnsureNotPastEnd()
    {
        if (PositionLong > LengthLong)
            ThrowHelper.ThrowEndOfStream();
    }

    public abstract T Read<T>() where T : unmanaged;
    public T Read<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where T : unmanaged where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return Read<T>();
    }

    public abstract void Read<T>(Span<T> dest) where T : unmanaged;
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

    public abstract string ReadString(int length, Encoding enc);
    public string ReadString<TOffset>(int length, Encoding enc, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadString(length, enc);
    }

    public abstract string ReadFString();
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
        if ((long)length * sizeof(int) > LengthLong - PositionLong)
            ThrowHelper.ThrowEndOfStream();
        if (length == 0)
            return [];
        ReaderHelpers.ValidateArrayLength(length);

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

    public abstract T[] ReadArray<T>(int length) where T : unmanaged;
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

    public T[] ReadArray<T>(Func<IGenericReader, T> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        return ReadArray(ReadLength(), getter);
    }
    public T[] ReadArray<T, TOffset>(Func<IGenericReader, T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
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
            EnsureNotPastEnd();
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

    public T[] ReadArray<T>(int length, Func<IGenericReader, T> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ReaderHelpers.ValidateGetterArrayLength<T>(length, MaxArrayAllocationSize);
        if (length == 0)
        {
            EnsureNotPastEnd();
            return [];
        }

        T[] result = GC.AllocateUninitializedArray<T>(length);

        for (int i = 0; i < length; i++)
            result[i] = getter(this);

        return result;
    }

    public T[] ReadArray<T, TOffset>(int length, Func<IGenericReader, T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
        where TOffset : IBinaryInteger<TOffset>
    {
        SeekVoid(offset, origin);
        return ReadArray(length, getter);
    }

    // Runs once, however often Dispose is called.
    protected virtual void Dispose(bool disposing) { }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

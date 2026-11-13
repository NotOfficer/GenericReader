using System.Numerics;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace GenericReader;

public interface IGenericReader : IDisposable
{
    public static GenericBufferReader Create(byte[] buffer, int start, int length) => new(buffer, start, length);
    public static GenericBufferReader Create(byte[] buffer) => new(buffer);
    public static GenericBufferReader Create(ReadOnlyMemory<byte> memory) => new(memory);
    public static GenericBufferReader Create(Memory<byte> memory) => new(memory);

    public static GenericSpanReader Create(ReadOnlySpan<byte> span) => new(span);
    public static GenericSpanReader Create(Span<byte> span) => new(span);

    public static GenericFileReader Create(string filePath) => new(filePath);
    public static GenericFileReader Create(SafeFileHandle handle, bool leaveOpen = false) => new(handle, leaveOpen: leaveOpen);

    public static GenericStreamReader Create(Stream stream, bool leaveOpen = false) => new(stream, leaveOpen);

    // The array size in bytes that ReadArray with a getter allocates at most (256 MiB). Such arrays are allocated before
    // any element is read, and an element may take no bytes at all, so their length cannot be checked against the data
    // like the others.
    public const long DefaultMaxArrayAllocationSize = 1L << 28;

    long MaxArrayAllocationSize { get; set; }

    int Position { get; set; }
    long PositionLong { get; set; }
    TPosition GetPosition<TPosition>() where TPosition : IBinaryInteger<TPosition>;
    void SetPosition<TPosition>(TPosition position) where TPosition : IBinaryInteger<TPosition>;
    int Length { get; }
    long LengthLong { get; }
    TLength GetLength<TLength>() where TLength : IBinaryInteger<TLength>;
    int Seek<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    long SeekLong<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    void SeekVoid<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    T Read<T>() where T : unmanaged;
    T Read<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged where TOffset : IBinaryInteger<TOffset>;
    void Read<T>(Span<T> dest) where T : unmanaged;
    void Read<T, TOffset>(Span<T> dest, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged where TOffset : IBinaryInteger<TOffset>;
    bool ReadBoolean();
    bool ReadBoolean<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    string ReadString(Encoding enc);
    string ReadString<TOffset>(Encoding enc, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    string ReadString(int length, Encoding enc);
    string ReadString<TOffset>(int length, Encoding enc, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    string ReadFString();
    string ReadFString<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    string[] ReadFStringArray();
    string[] ReadFStringArray<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    string[] ReadFStringArray(int length);
    string[] ReadFStringArray<TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    T[] ReadArray<T>() where T : unmanaged;
    T[] ReadArray<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged where TOffset : IBinaryInteger<TOffset>;
    T[] ReadArray<T>(int length) where T : unmanaged;
    T[] ReadArray<T, TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged where TOffset : IBinaryInteger<TOffset>;
    T[] ReadArray<T>(Func<T> getter);
    T[] ReadArray<T, TOffset>(Func<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    T[] ReadArray<T>(Func<IGenericReader, T> getter);
    T[] ReadArray<T, TOffset>(Func<IGenericReader, T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    T[] ReadArray<T>(int length, Func<T> getter);
    T[] ReadArray<T, TOffset>(int length, Func<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
    T[] ReadArray<T>(int length, Func<IGenericReader, T> getter);
    T[] ReadArray<T, TOffset>(int length, Func<IGenericReader, T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current) where TOffset : IBinaryInteger<TOffset>;
}

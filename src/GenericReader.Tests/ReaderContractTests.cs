using System.Runtime.CompilerServices;
using System.Text;

namespace GenericReader.Tests;

public delegate void ReadAction<TReader>(ref TReader reader) where TReader : IGenericReader, allows ref struct;

// Every reader has to fail the same way for the same data: EndOfStreamException when the data runs out,
// InvalidDataException for a length in the data that cannot be valid, ArgumentOutOfRangeException for invalid
// arguments. A failed read does not move Position; only a length prefix that was read before the failure stays read.
public abstract class ReaderContractTests<TReader> : IDisposable where TReader : IGenericReader, allows ref struct
{
    private readonly List<string> _files = [];

    protected abstract TReader Create(byte[] data);

    protected string CreateFile(byte[] data)
    {
        string path = Path.GetTempFileName();
        _files.Add(path);
        File.WriteAllBytes(path, data);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in _files)
            File.Delete(path);
    }

    private void AssertThrows<TException>(byte[] data, long position, ReadAction<TReader> read, long? positionAfter = null)
        where TException : Exception
    {
        TReader reader = Create(data);
        try
        {
            reader.PositionLong = position;
            Exception? thrown = null;
            try
            {
                read(ref reader);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.IsType<TException>(thrown);
            Assert.Equal(positionAfter ?? position, reader.PositionLong);
        }
        finally
        {
            reader.Dispose();
        }
    }

    // a valid read, and where it leaves Position
    private void AssertReads(byte[] data, ReadAction<TReader> read, long positionAfter, long position = 0)
    {
        TReader reader = Create(data);
        try
        {
            reader.PositionLong = position;
            read(ref reader);
            Assert.Equal(positionAfter, reader.PositionLong);
        }
        finally
        {
            reader.Dispose();
        }
    }

    private void AssertReadNearEnd<T>() where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        byte[] data = new byte[64];

        for (int left = 0; left < size; left++)
            AssertThrows<EndOfStreamException>(data, data.Length - left, (ref r) => r.Read<T>());

        TReader reader = Create(data);
        try
        {
            reader.Position = data.Length - size;
            reader.Read<T>();
            Assert.Equal(data.Length, reader.Position);
        }
        finally
        {
            reader.Dispose();
        }
    }

    [Fact]
    public void ReadThrowsWhenFewerThanSizeOfTBytesAreLeft()
    {
        AssertReadNearEnd<byte>();
        AssertReadNearEnd<short>();
        AssertReadNearEnd<int>();
        AssertReadNearEnd<long>();
        AssertReadNearEnd<Guid>();
        AssertReadNearEnd<decimal>();
        AssertReadNearEnd<Int128>();
    }

    [Theory]
    [InlineData(17)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void ReadsPastTheEndThrow(int position)
    {
        byte[] data = new byte[16];
        AssertThrows<EndOfStreamException>(data, position, (ref r) => r.Read<int>());
        AssertThrows<EndOfStreamException>(data, position, (ref r) => r.Read<byte>());
        AssertThrows<EndOfStreamException>(data, position, (ref r) => r.ReadArray<byte>(1));
        AssertThrows<EndOfStreamException>(data, position, (ref r) => r.ReadString(1, Encoding.UTF8));
        AssertThrows<EndOfStreamException>(data, position, (ref r) => r.ReadFString());
        AssertThrows<EndOfStreamException>(data, position, (ref r) => r.Read<byte>(new byte[1]));
        // past the end, not even zero bytes can be read
        AssertThrows<EndOfStreamException>(data, position, (ref r) => r.Read(Span<byte>.Empty));
    }

    [Fact]
    public void ZeroLengthReadsPastTheEndThrow()
    {
        byte[] data = new byte[16];
        ReadAction<TReader>[] reads =
        [
            (ref r) => r.Read(Span<byte>.Empty),
            (ref r) => r.ReadArray<byte>(0),
            (ref r) => r.ReadString(0, Encoding.UTF8),
            (ref r) => r.ReadFStringArray(0),
            (ref r) => r.ReadArray(0, () => 0),
        ];

        foreach (ReadAction<TReader> read in reads)
        {
            AssertThrows<EndOfStreamException>(data, 17, read);
            // at the end, there are zero bytes to read
            AssertReads(data, read, positionAfter: 16, position: 16);
        }
    }

    [Fact]
    public void NullArgumentsThrow()
    {
        byte[] data = [.. TestBytes.Int32(4), .. new byte[12]];
        AssertThrows<ArgumentNullException>(data, 0, (ref r) => r.ReadString(null!));
        AssertThrows<ArgumentNullException>(data, 0, (ref r) => r.ReadString(4, null!));
    }

    [Fact]
    public void ADecoderErrorDoesNotMovePosition()
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        byte[] data = [.. TestBytes.Int32(1), 0xFF];
        AssertThrows<DecoderFallbackException>(data, 4, (ref r) => r.ReadString(1, strict));
        AssertThrows<DecoderFallbackException>(data, 0, (ref r) => r.ReadString(strict), positionAfter: 4);
    }

    [Fact]
    public void ReadsWithAnOffset()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        AssertReads(data, (ref r) => Assert.Equal(0x08070605, r.Read<int, int>(4, SeekOrigin.Begin)), positionAfter: 8);
        AssertReads(data, (ref r) => Assert.Equal(new byte[] { 11, 12 }, r.ReadArray<byte, int>(2, -2, SeekOrigin.End)), positionAfter: 12);

        // the seek is a step of its own: when the read after it fails, Position stays where the seek moved it
        AssertThrows<EndOfStreamException>(data, 0, (ref r) => r.Read<int, int>(10, SeekOrigin.Begin), positionAfter: 10);
        // an invalid seek fails before anything moves
        AssertThrows<ArgumentOutOfRangeException>(data, 3, (ref r) => r.Read<int, int>(-4, SeekOrigin.Current));
    }

    [Fact]
    public void TruncatedReadsThrow()
    {
        byte[] data = new byte[16];

        AssertThrows<EndOfStreamException>(data, 8, (ref r) => r.ReadArray<byte>(9));
        AssertThrows<EndOfStreamException>(data, 4, (ref r) => r.ReadArray<int>(4));
        AssertThrows<EndOfStreamException>(data, 14, (ref r) => r.ReadBoolean());
        AssertThrows<EndOfStreamException>(data, 9, (ref r) => r.ReadString(8, Encoding.UTF8));
        // with a length prefix: the prefix stays read
        AssertThrows<EndOfStreamException>([.. TestBytes.Int32(13), .. new byte[12]], 0,
            (ref r) => r.ReadArray<byte>(), positionAfter: 4);
        AssertThrows<EndOfStreamException>([.. TestBytes.Int32(13), .. new byte[12]], 0,
            (ref r) => r.ReadString(Encoding.UTF8), positionAfter: 4);
    }

    [Fact]
    public void AFailedReadLeavesTheDestinationAlone()
    {
        byte[] data = [1, 2, 3, 4, 5, 6];
        int[] dest = [-1, -1];
        AssertThrows<EndOfStreamException>(data, 0, (ref r) => r.Read<int>(dest));
        Assert.Equal(new[] { -1, -1 }, dest);
    }

    [Fact]
    public void ReadIntoADestination()
    {
        byte[] data = [1, 0, 0, 0, 2, 0, 0, 0];
        TReader reader = Create(data);
        try
        {
            reader.Position = 3;
            reader.Read(Span<int>.Empty);
            Assert.Equal(3, reader.Position);
            reader.Position = data.Length;
            reader.Read(Span<byte>.Empty);
            Assert.Equal(data.Length, reader.Position);

            int[] dest = new int[2];
            reader.Position = 0;
            reader.Read<int>(dest);
            Assert.Equal(new[] { 1, 2 }, dest);
            Assert.Equal(data.Length, reader.Position);
        }
        finally
        {
            reader.Dispose();
        }
    }

    [Fact]
    public void ByteCountsThatOverflowThrow()
    {
        byte[] data = new byte[16];
        // 0x40000001 * 4 used to wrap around to 4 bytes
        AssertThrows<EndOfStreamException>(data, 0, (ref r) => r.ReadArray<int>(0x4000_0001));
        AssertThrows<EndOfStreamException>(data, 0, (ref r) => r.ReadArray<long>(int.MaxValue));
        AssertThrows<EndOfStreamException>(data, 0, (ref r) => r.ReadArray<Int128>(int.MaxValue));
    }

    [Fact]
    public void ACorruptedLengthAllocatesNothing()
    {
        byte[] data = new byte[16];
        byte[] one = TestBytes.FString("abc", false);

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        AssertThrows<EndOfStreamException>(data, 0, (ref r) => r.ReadArray<byte>(1 << 30));
        AssertThrows<EndOfStreamException>(data, 0, (ref r) => r.ReadString(1 << 30, Encoding.UTF8));
        AssertThrows<EndOfStreamException>([.. TestBytes.Int32(1 << 30), .. data], 0,
            (ref r) => r.ReadFString(), positionAfter: 4);
        // int.MaxValue strings would have allocated a 16 GiB array before reading the first one
        AssertThrows<EndOfStreamException>([.. TestBytes.Int32(int.MaxValue), .. one], 0,
            (ref r) => r.ReadFStringArray(), positionAfter: 4);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1 << 20);
    }

    [Fact]
    public void NegativeArgumentsThrow()
    {
        byte[] data = new byte[16];
        AssertThrows<ArgumentOutOfRangeException>(data, 0, (ref r) => r.ReadArray<byte>(-1));
        AssertThrows<ArgumentOutOfRangeException>(data, 0, (ref r) => r.ReadArray<int>(int.MinValue));
        AssertThrows<ArgumentOutOfRangeException>(data, 0, (ref r) => r.ReadString(-1, Encoding.UTF8));
        AssertThrows<ArgumentOutOfRangeException>(data, 0, (ref r) => r.ReadFStringArray(-1));
        AssertThrows<ArgumentOutOfRangeException>(data, 0, (ref r) => r.ReadArray(-1, () => 0));
    }

    [Fact]
    public void NegativeLengthPrefixesAreInvalidData()
    {
        byte[] data = [.. TestBytes.Int32(-1), .. new byte[12]];
        AssertThrows<InvalidDataException>(data, 0, (ref r) => r.ReadArray<byte>(), positionAfter: 4);
        AssertThrows<InvalidDataException>(data, 0, (ref r) => r.ReadString(Encoding.UTF8), positionAfter: 4);
        AssertThrows<InvalidDataException>(data, 0, (ref r) => r.ReadFStringArray(), positionAfter: 4);
        AssertThrows<InvalidDataException>(data, 0, (ref r) => r.ReadArray(() => 0), positionAfter: 4);
    }

    [Fact]
    public void ReadFStringAtTheEndOfTheData()
    {
        // not even the length
        AssertThrows<EndOfStreamException>([4, 0, 0], 0, (ref r) => r.ReadFString());
        // UTF-8: one byte missing
        AssertThrows<EndOfStreamException>(TestBytes.FString("abc", false)[..^1], 0,
            (ref r) => r.ReadFString(), positionAfter: 4);
        // UTF-16: the characters are there but the terminator is not
        AssertThrows<EndOfStreamException>(TestBytes.FString("abc", true)[..^2], 0,
            (ref r) => r.ReadFString(), positionAfter: 4);
        AssertThrows<EndOfStreamException>(TestBytes.FString("abc", true)[..^1], 0,
            (ref r) => r.ReadFString(), positionAfter: 4);
        // UTF-16 empty string without its terminator
        AssertThrows<EndOfStreamException>(TestBytes.Int32(-1), 0, (ref r) => r.ReadFString(), positionAfter: 4);

        // complete strings that end exactly at the end of the data
        foreach (bool unicode in new[] { false, true })
        {
            byte[] data = TestBytes.FString("abc", unicode);
            TReader reader = Create(data);
            try
            {
                Assert.Equal("abc", reader.ReadFString());
                Assert.Equal(data.Length, reader.Position);
            }
            finally
            {
                reader.Dispose();
            }
        }
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-0x4000_0000)]
    public void FStringLengthsWhoseByteCountDoesNotFitAreInvalidData(int length)
    {
        AssertThrows<InvalidDataException>([.. TestBytes.Int32(length), .. new byte[16]], 0,
            (ref r) => r.ReadFString(), positionAfter: 4);
    }

    [Fact]
    public void TheLargestUtf16FStringLengthIsOnlyShortOfData()
    {
        // -0x3FFFFFFF chars are 0x7FFFFFFE bytes, which fit in an int: this data is merely too short
        AssertThrows<EndOfStreamException>([.. TestBytes.Int32(-0x3FFF_FFFF), .. new byte[16]], 0,
            (ref r) => r.ReadFString(), positionAfter: 4);
    }

    [Fact]
    public void ReadFStringArray()
    {
        byte[] one = TestBytes.FString("abc", false);

        // a count that fits the data size but runs out of strings
        byte[] two = [.. TestBytes.Int32(2), .. one];
        AssertThrows<EndOfStreamException>(two, 0, (ref r) => r.ReadFStringArray(), positionAfter: two.Length);

        byte[] both = [.. TestBytes.Int32(2), .. one, .. TestBytes.FString("de", true)];
        TReader reader = Create(both);
        try
        {
            Assert.Equal(new[] { "abc", "de" }, reader.ReadFStringArray());
            Assert.Equal(both.Length, reader.Position);
            reader.Position = 0;
            Assert.Empty(reader.ReadFStringArray(0));
        }
        finally
        {
            reader.Dispose();
        }
    }

    // the exception that read throws, or null; a lambda cannot capture a ref struct, so the reader is passed by ref
    private static Exception? Record(ref TReader reader, ReadAction<TReader> read)
    {
        try
        {
            read(ref reader);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void GetterArraysAreLimitedToMaxArrayAllocationSize()
    {
        byte[] data = new byte[16];
        TReader reader = Create(data);
        try
        {
            Assert.Equal(IGenericReader.DefaultMaxArrayAllocationSize, reader.MaxArrayAllocationSize);

            // rejected before the array is allocated or the getter runs
            int calls = 0;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            Assert.IsType<InvalidDataException>(Record(ref reader, (ref r) => r.ReadArray(int.MaxValue, () => ++calls)));
            // the limit is in bytes: 2^24 elements of 256 bytes used to allocate 4 GiB from a 4-byte count
            Assert.IsType<InvalidDataException>(Record(ref reader, (ref r) => r.ReadArray(1 << 24, () => default(Big256))));
            Assert.IsType<InvalidDataException>(Record(ref reader, (ref r) => r.ReadArray(1 << 26, () => "")));
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1 << 20);
            Assert.Equal(0, calls);

            reader.MaxArrayAllocationSize = 3 * sizeof(int);
            Assert.IsType<InvalidDataException>(Record(ref reader, (ref r) => r.ReadArray(4, () => ++calls)));
            Assert.Equal(0, calls);
            Assert.Equal(new[] { 1, 2, 3 }, reader.ReadArray(3, () => ++calls));

            // zero-byte elements: the limit is the only bound
            reader.MaxArrayAllocationSize = 100 * sizeof(int);
            Assert.Equal(100, reader.ReadArray(100, () => 0).Length);
            Assert.Equal(0, reader.Position);

            Assert.IsType<ArgumentOutOfRangeException>(Record(ref reader, (ref r) => r.MaxArrayAllocationSize = -1));
            Assert.Equal(100 * sizeof(int), reader.MaxArrayAllocationSize);
            Assert.IsType<ArgumentNullException>(Record(ref reader, (ref r) => r.ReadArray(1, (Func<int>)null!)));
            Assert.IsType<ArgumentNullException>(Record(ref reader, (ref r) => r.ReadArray((Func<int>)null!)));
            Assert.Equal(0, reader.Position);
        }
        finally
        {
            reader.Dispose();
        }

        // with a length prefix
        AssertThrows<InvalidDataException>([.. TestBytes.Int32(int.MaxValue), .. data], 0,
            (ref r) => r.ReadArray(() => 0), positionAfter: 4);
    }

    [Fact]
    public void InvalidPositionsThrow()
    {
        TReader reader = Create(new byte[16]);
        try
        {
            reader.Position = 5;
            ReadAction<TReader>[] invalid =
            [
                (ref r) => r.Position = -1,
                (ref r) => r.PositionLong = long.MinValue,
                (ref r) => r.SetPosition(Int128.MinValue),
                (ref r) => r.SetPosition(UInt128.MaxValue),
                (ref r) => r.Seek(-6, SeekOrigin.Current),
                (ref r) => r.Seek(-1, SeekOrigin.Begin),
                (ref r) => r.Seek(-17, SeekOrigin.End),
                // these used to wrap around to a negative or a small position
                (ref r) => r.SeekLong(long.MaxValue, SeekOrigin.Current),
                (ref r) => r.SeekLong(UInt128.MaxValue, SeekOrigin.Current),
                (ref r) => r.SeekLong(Int128.MinValue, SeekOrigin.End),
                (ref r) => r.Seek(0, (SeekOrigin)3),
                // the int overload cannot return this; it used to move Position first on the file and stream readers
                (ref r) => r.Seek(1L << 31, SeekOrigin.Begin),
            ];

            foreach (ReadAction<TReader> action in invalid)
            {
                Assert.IsType<ArgumentOutOfRangeException>(Record(ref reader, action));
                Assert.Equal(5, reader.Position);
            }

            // like a stream, a position past the end is allowed, only reading from it fails
            Assert.Equal(100, reader.Seek(95));
            Assert.Equal(16, reader.Seek(0, SeekOrigin.End));
            Assert.Equal(2, reader.Seek(-14, SeekOrigin.Current));
            reader.SetPosition(7u);
            Assert.Equal(7, reader.GetPosition<long>());
        }
        finally
        {
            reader.Dispose();
        }
    }
}

[InlineArray(256)]
public struct Big256
{
    private byte _element;
}

public class SpanReaderContractTests : ReaderContractTests<GenericSpanReader>
{
    protected override GenericSpanReader Create(byte[] data) => new(data);
}

public class BufferReaderContractTests : ReaderContractTests<GenericBufferReader>
{
    protected override GenericBufferReader Create(byte[] data) => new(data);
}

public class StreamReaderContractTests : ReaderContractTests<GenericStreamReader>
{
    protected override GenericStreamReader Create(byte[] data) => new(new MemoryStream(data));
}

public class FileReaderContractTests : ReaderContractTests<GenericFileReader>
{
    protected override GenericFileReader Create(byte[] data) => new(CreateFile(data));
}

// a cache smaller than most reads, so that they go straight to the file
public class SmallBufferFileReaderContractTests : ReaderContractTests<GenericFileReader>
{
    protected override GenericFileReader Create(byte[] data) => new(CreateFile(data), bufferSize: 3);
}

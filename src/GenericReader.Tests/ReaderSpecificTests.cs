using System.Buffers;
using System.Text;

using Microsoft.Win32.SafeHandles;

namespace GenericReader.Tests;

public class SpanReaderSpecificTests
{
    [Fact]
    public void ReadDoesNotReadPastASlice()
    {
        // the reader sees the first 8 bytes; the old check let Read<int> at 6 take two of the 0xFF bytes
        byte[] buffer = [1, 2, 3, 4, 5, 6, 7, 8, 0xFF, 0xFF, 0xFF, 0xFF];
        var reader = new GenericSpanReader(buffer, 0, 8) { Position = 6 };
        Assert.Equal(0x0807, reader.Read<short>());
        Assert.Throws<EndOfStreamException>(() => new GenericSpanReader(buffer, 0, 8) { Position = 6 }.Read<int>());
    }

    [Fact]
    public void ReadSpanStaysInsideTheData()
    {
        byte[] data = new byte[16];
        var reader = new GenericSpanReader(data) { Position = 12 };

        // 0x40000001 * 4 wrapped around to 4 bytes, yet the span came back with 0x40000001 elements
        Assert.Throws<EndOfStreamException>(() => new GenericSpanReader(data).ReadSpan<int>(0x4000_0001));
        Assert.Throws<EndOfStreamException>(() => new GenericSpanReader(data) { Position = 12 }.ReadSpan<int>(2));
        Assert.Throws<EndOfStreamException>(() => new GenericSpanReader(data) { Position = 12 }.ReadSpan(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericSpanReader(data).ReadSpan(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericSpanReader(data).ReadSpan<int>(-0x4000_0000));
        Assert.Throws<InvalidDataException>(() => new GenericSpanReader(TestBytes.Int32(-1)).ReadSpan());

        Assert.Equal(1, reader.ReadSpan<int>(1).Length);
        Assert.Equal(16, reader.Position);
        Assert.Equal(0, reader.ReadSpan(0).Length);
    }

    [Fact]
    public void GetterFuncArraysAreLimitedToMaxArrayAllocationSize()
    {
        byte[] data = [1, 2, 3, 4];
        var reader = new GenericSpanReader(data) { MaxArrayAllocationSize = 3 };

        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadArray(3, static (ref r) => r.Read<byte>()));
        Assert.Throws<InvalidDataException>(() =>
            new GenericSpanReader(data) { MaxArrayAllocationSize = 3 }.ReadArray(4, static (ref r) => r.Read<byte>()));
    }

    [Fact]
    public void ADefaultReaderHasTheDefaultLimit()
    {
        // the limit used to be 0 here, so that every getter array failed
        GenericSpanReader reader = default;
        Assert.Equal(IGenericReader.DefaultMaxArrayAllocationSize, reader.MaxArrayAllocationSize);
        Assert.Equal(0, reader.Length);
    }

    [Fact]
    public void SlicesKeepMaxArrayAllocationSize()
    {
        var reader = new GenericSpanReader(new byte[16]) { MaxArrayAllocationSize = 5, Position = 4 };
        Assert.Equal(5, reader.Slice(2).MaxArrayAllocationSize);
        Assert.Equal(5, reader.Slice(2, 4, sliceAtPosition: true).MaxArrayAllocationSize);
    }

    [Fact]
    public void ReadsReadOnlyData()
    {
        // read-only memory (here in the assembly image) used to come back as writable spans
        var reader = new GenericSpanReader("abcd"u8);
        ReadOnlySpan<byte> span = reader.ReadSpan(2);
        Assert.Equal((byte)'a', span[0]);
        Assert.Equal("cd", reader.ReadString(2, Encoding.UTF8));
    }

    [Fact]
    public void GettersTakingIGenericReaderThrowThroughTheInterface()
    {
        // [Obsolete(error: true)] does not stop a call through a generic TReader; these returned an uninitialized
        // array without calling the getter
        Assert.Throws<NotSupportedException>(() => ReadThroughInterface(new GenericSpanReader(new byte[16])));
    }

    private static int[] ReadThroughInterface<TReader>(TReader reader) where TReader : IGenericReader, allows ref struct
    {
        return reader.ReadArray(4, static r => r.Read<int>());
    }
}

public class BufferReaderSpecificTests
{
    [Fact]
    public void ReadsDoNotLeaveTheMemory()
    {
        // the reader sees the first 8 bytes; the old checks let reads take the 0xFF bytes behind them
        byte[] buffer = [1, 2, 3, 4, 5, 6, 7, 8, 0xFF, 0xFF, 0xFF, 0xFF];
        var reader = new GenericBufferReader(buffer, 0, 8) { Position = 6 };

        Assert.Throws<EndOfStreamException>(() => reader.Read<int>());
        Assert.Throws<EndOfStreamException>(() => reader.ReadArray<byte>(3));
        Assert.Throws<EndOfStreamException>(() => reader.ReadSpan<int>(0x4000_0001));
        Assert.Throws<EndOfStreamException>(() => reader.ReadSpan(3));
        Assert.Throws<EndOfStreamException>(() => reader.ReadMemory(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadMemory(-1));
        Assert.Equal(6, reader.Position);

        Assert.Equal(new byte[] { 7, 8 }, reader.ReadMemory(2).ToArray());
        Assert.Equal(8, reader.Position);
    }

    [Fact]
    public void ReadFStringMemory()
    {
        // UTF-16 without its terminator used to succeed and leave Position past the end
        var reader = new GenericBufferReader(TestBytes.FString("abc", true)[..^2]);
        Assert.Throws<EndOfStreamException>(() => reader.ReadFStringMemory());
        Assert.Equal(4, reader.Position);

        reader = new GenericBufferReader([.. TestBytes.Int32(int.MinValue), .. new byte[16]]);
        Assert.Throws<InvalidDataException>(() => reader.ReadFStringMemory());

        foreach (bool unicode in new[] { false, true })
        {
            byte[] data = TestBytes.FString("abc", unicode);
            reader = new GenericBufferReader(data);
            FStringMemory value = reader.ReadFStringMemory();
            Assert.Equal("abc", value.ToString());
            Assert.Equal(unicode, value.IsUnicode);
            Assert.Equal(data.Length, reader.Position);
        }
    }

    [Fact]
    public void ReadFStringMemoryArrayWithACountBeyondTheData()
    {
        byte[] data = TestBytes.FString("abc", true);

        // int.MaxValue strings would have allocated the array before reading the first one
        var reader = new GenericBufferReader([.. TestBytes.Int32(int.MaxValue), .. data]);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<EndOfStreamException>(() => reader.ReadFStringMemoryArray());
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1 << 20);
        Assert.Equal(4, reader.Position);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadFStringMemoryArray(-1));
        Assert.Throws<InvalidDataException>(() => new GenericBufferReader(TestBytes.Int32(-1)).ReadFStringMemoryArray());

        reader = new GenericBufferReader([.. TestBytes.Int32(1), .. data]);
        Assert.Equal("abc", Assert.Single(reader.ReadFStringMemoryArray()).ToString());
    }

    [Fact]
    public void SlicesKeepMaxArrayAllocationSize()
    {
        var reader = new GenericBufferReader(new byte[16]) { MaxArrayAllocationSize = 5 };
        Assert.Equal(5, reader.Slice(2).MaxArrayAllocationSize);
        Assert.Equal(5, reader.Slice(2, 4).MaxArrayAllocationSize);
    }

    [Fact]
    public void ReadMemory()
    {
        var reader = new GenericBufferReader([.. TestBytes.Int32(3), 7, 8, 9, 10]);
        Assert.Equal(new byte[] { 7, 8, 9 }, reader.ReadMemory().ToArray());
        Assert.Equal(new byte[] { 10 }, reader.AsMemory(sliceAtPosition: true).ToArray());
        Assert.Equal(8L, reader.GetLength<long>());
    }

    [Fact]
    public async Task LoadFromStreamReadsTheRestOfTheStream()
    {
        // the stream is not at its start: the loaders used to allocate its whole length and then run out of data
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        var stream = new MemoryStream(data) { Position = 4 };
        using (GenericBufferReader reader = GenericBufferReader.LoadFromStream(stream))
            Assert.Equal(data[4..], reader.AsSpan().ToArray());

        stream.Position = 4;
        using (GenericBufferReader reader = await GenericBufferReader.LoadFromStreamAsync(stream, TestContext.Current.CancellationToken))
            Assert.Equal(data[4..], reader.AsSpan().ToArray());
    }

    [Fact]
    public async Task LoadFromFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] data = new byte[100_000];
            Random.Shared.NextBytes(data);
            await File.WriteAllBytesAsync(path, data, TestContext.Current.CancellationToken);

            using (SafeFileHandle handle = File.OpenHandle(path))
            using (GenericBufferReader reader = GenericBufferReader.LoadFromFile(handle))
                Assert.Equal(data, reader.AsSpan().ToArray());

            using (SafeFileHandle handle = File.OpenHandle(path, options: FileOptions.Asynchronous))
            using (GenericBufferReader reader = await GenericBufferReader.LoadFromFileAsync(handle, TestContext.Current.CancellationToken))
                Assert.Equal(data, reader.AsSpan().ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class FileReaderSpecificTests : IDisposable
{
    private readonly string _filePath = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_filePath);
    }

    [Fact]
    public void ReadsDoNotSeeTheRestOfThePooledArray()
    {
        // leave a dirty array where the reader rents its buffer from: the old reader handed the whole rented array
        // to its inner reader, so reads past the end of the file returned these bytes instead of throwing
        byte[] dirty = ArrayPool<byte>.Shared.Rent(4096);
        dirty.AsSpan().Fill(0xCC);
        ArrayPool<byte>.Shared.Return(dirty);

        File.WriteAllBytes(_filePath, [1, 2, 3, 4, 5, 6]);
        using var reader = new GenericFileReader(_filePath);

        Assert.Throws<EndOfStreamException>(() => reader.Read<long>());
        Assert.Equal(0x04030201, reader.Read<int>());
        Assert.Throws<EndOfStreamException>(() => reader.Read<int>());
        Assert.Throws<EndOfStreamException>(() => reader.ReadBytes(3));
        Assert.Equal(4, reader.Position);
        Assert.Equal(0x0605, reader.Read<short>());
        Assert.Empty(reader.ReadBytes(0));
        Assert.Equal(6, reader.Position);
    }

    [Fact]
    public void ReadBytes()
    {
        byte[] data = new byte[10_000];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(_filePath, data);
        using var reader = new GenericFileReader(_filePath);

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<EndOfStreamException>(() => reader.ReadBytes(1 << 30));
        Assert.Throws<EndOfStreamException>(() => reader.ReadBytes(1 << 30, useSharedArrayPool: true));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1 << 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadBytes(-1));

        // from the cache, and larger than it
        Assert.Equal(data[..100], reader.ReadBytes(100));
        Assert.Equal(data[100..9000], reader.ReadBytes(8900));
        byte[] pooled = reader.ReadBytes(1000, useSharedArrayPool: true);
        Assert.Equal(data[9000..], pooled[..1000]);
        ArrayPool<byte>.Shared.Return(pooled);
        Assert.Equal(data.Length, reader.Position);
    }

    [Fact]
    public void ALargeReadDoesNotBreakTheCache()
    {
        byte[] data = new byte[64 * 1024];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(_filePath, data);
        using var reader = new GenericFileReader(_filePath, bufferSize: 256);

        Assert.Equal(data[..1000], reader.ReadArray<byte>(1000));
        Assert.Equal(Encoding.UTF8.GetString(data, 1000, 48 * 1024), reader.ReadString(48 * 1024, Encoding.UTF8));

        // small reads around and across the cache end, and backwards
        foreach (int position in new[] { 0, 252, 253, 255, 256, 1000, 50_000, 3, 65_532 })
        {
            reader.Position = position;
            Assert.Equal(BitConverter.ToInt32(data, position), reader.Read<int>());
            Assert.Equal(position + 4, reader.Position);
        }
    }

    [Fact]
    public void AFileThatGrowsIsOnlyReadUpToItsLength()
    {
        File.WriteAllBytes(_filePath, new byte[100]);
        using var reader = new GenericFileReader(OpenShared());

        using (var writer = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            writer.Write(new byte[100]);

        // the cache used to fill past Length, so that the bytes behind it could be read from the cache
        reader.Position = 96;
        reader.Read<int>();
        Assert.Throws<EndOfStreamException>(() => reader.Read<int>());
        reader.Position = 50;
        reader.Read<int>();
        reader.Position = 150;
        Assert.Throws<EndOfStreamException>(() => reader.Read<int>());
        Assert.Equal(100, reader.Length);
    }

    [Fact]
    public void AHugePositionDoesNotWrapAroundIntoTheCache()
    {
        File.WriteAllBytes(_filePath, new byte[32]);
        using var reader = new GenericFileReader(_filePath);
        reader.Read<int>(); // the cache holds the whole file

        // position + size used to overflow and pass the cache check, which then read at a garbage offset
        foreach (long position in new[] { long.MaxValue, long.MaxValue - 1, long.MaxValue - 15, 1L << 32 })
        {
            reader.PositionLong = position;
            Assert.Throws<EndOfStreamException>(() => reader.Read<int>());
            Assert.Throws<EndOfStreamException>(() => reader.ReadArray<byte>(16));
            Assert.Equal(position, reader.PositionLong);
        }
    }

    [Fact]
    public void AFileThatGetsShorterThrows()
    {
        File.WriteAllBytes(_filePath, new byte[16]);
        using var cached = new GenericFileReader(OpenShared());
        using var direct = new GenericFileReader(OpenShared(), bufferSize: 1);

        using (var writer = new FileStream(_filePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            writer.SetLength(6);

        // through the cache, and past it
        Assert.Throws<EndOfStreamException>(() => cached.Read<long>());
        Assert.Equal(0, cached.Position);
        Assert.Throws<EndOfStreamException>(() => direct.Read<long>());
        Assert.Equal(0, direct.Position);
    }

    private SafeFileHandle OpenShared() =>
        File.OpenHandle(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.RandomAccess);

    [Fact]
    public void DisposeIsIdempotentAndStopsReads()
    {
        File.WriteAllBytes(_filePath, new byte[10_000]);
        var pool = new CountingPool();
        var reader = new GenericFileReader(_filePath) { BufferPool = pool };
        reader.Read<int>();
        reader.Position = 9000; // a cache miss refills the same array instead of renting another
        reader.Read<int>();
        Assert.Equal(1, pool.Rented);

        // a second Dispose used to return the cache array to the pool again
        reader.Dispose();
        reader.Dispose();
        Assert.Equal(1, pool.Returned);

        // the cache used to keep serving the array that went back to the pool
        Assert.Throws<ObjectDisposedException>(() => reader.Read<int>());
        Assert.Throws<ObjectDisposedException>(() => reader.ReadBytes(8000));
        Assert.Equal(1, pool.Rented);
    }

    // Counts rents and returns, and fails on returning an array that is not rented (a double return).
    private sealed class CountingPool : ArrayPool<byte>
    {
        private readonly HashSet<byte[]> _rented = new(ReferenceEqualityComparer.Instance);

        public int Rented { get; private set; }
        public int Returned { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            byte[] array = new byte[minimumLength];
            _rented.Add(array);
            Rented++;
            return array;
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.True(_rented.Remove(array), "The array was returned twice or was not rented here.");
            Returned++;
        }
    }

    [Fact]
    public void BufferSizeHasToBePositive()
    {
        File.WriteAllBytes(_filePath, new byte[16]);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericFileReader(_filePath, bufferSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericFileReader(_filePath, bufferSize: -1));

        // the rejected readers left no open handle behind
        File.Delete(_filePath);
    }

    [Fact]
    public void LeaveOpenKeepsTheHandle()
    {
        File.WriteAllBytes(_filePath, [1, 2, 3, 4]);
        using SafeFileHandle handle = OpenShared();

        using (var reader = new GenericFileReader(handle, leaveOpen: true))
            Assert.Equal(0x04030201, reader.Read<int>());
        Assert.False(handle.IsClosed);

        using (GenericFileReader reader = IGenericReader.Create(handle, leaveOpen: true))
            reader.Read<int>();
        Assert.False(handle.IsClosed);

        new GenericFileReader(handle).Dispose();
        Assert.True(handle.IsClosed);
    }
}

public class StreamReaderSpecificTests
{
    [Fact]
    public void ReadBytes()
    {
        byte[] data = new byte[16];
        Random.Shared.NextBytes(data);
        using var reader = new GenericStreamReader(new MemoryStream(data));

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<EndOfStreamException>(() => reader.ReadBytes(1 << 30));
        Assert.Throws<EndOfStreamException>(() => reader.ReadBytes(1 << 30, useSharedArrayPool: true));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 1 << 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadBytes(-1));
        Assert.Equal(0, reader.Position);

        Assert.Equal(data[..10], reader.ReadBytes(10));
        byte[] pooled = reader.ReadBytes(6, useSharedArrayPool: true);
        Assert.Equal(data[10..], pooled[..6]);
        ArrayPool<byte>.Shared.Return(pooled);
    }

    [Fact]
    public void AStreamThatIsNotAtItsStart()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        using var reader = new GenericStreamReader(new MemoryStream(data) { Position = 4 });
        Assert.Equal(4, reader.Position);
        Assert.Equal(8, reader.Length);
        Assert.Equal(0x08070605, reader.Read<int>());
        Assert.Throws<EndOfStreamException>(() => reader.Read<byte>());
    }

    [Fact]
    public void AStreamThatEndsBeforeItsLengthPutsPositionBack()
    {
        byte[] data = [.. TestBytes.Int32(8), 1, 2, 3, 4];
        using var reader = new GenericStreamReader(new ShortStream(data, reportedLength: 16));

        Assert.Throws<EndOfStreamException>(() => reader.Read<long, int>(4, SeekOrigin.Begin));
        Assert.Equal(4, reader.Position);
        Assert.Throws<EndOfStreamException>(() => reader.ReadArray<byte>(8));
        Assert.Equal(4, reader.Position);

        reader.Position = 0;
        Assert.Throws<EndOfStreamException>(() => reader.ReadFString());
        Assert.Equal(4, reader.Position); // after the length prefix, like the other readers
    }

    // a stream whose Length claims more data than it holds
    private sealed class ShortStream(byte[] data, long reportedLength) : MemoryStream(data)
    {
        public override long Length => reportedLength;
    }

    [Fact]
    public void LongStringsGoThroughThePool()
    {
        string value = new('x', 5000);
        byte[] data = [.. TestBytes.FString(value, false), .. TestBytes.FString(value, true)];
        using var reader = new GenericStreamReader(new MemoryStream(data));

        Assert.Equal(value, reader.ReadFString());
        Assert.Equal(value, reader.ReadFString());
        Assert.Equal(data.Length, reader.Position);
    }

    [Fact]
    public void LeaveOpenKeepsTheStream()
    {
        var stream = new MemoryStream([1, 2, 3, 4]);

        using (var reader = new GenericStreamReader(stream, leaveOpen: true))
            Assert.Equal(0x04030201, reader.Read<int>());
        Assert.True(stream.CanRead);

        using (GenericStreamReader reader = IGenericReader.Create(stream, leaveOpen: true))
            Assert.Equal(4, reader.Position);
        Assert.True(stream.CanRead);

        new GenericStreamReader(stream).Dispose();
        Assert.False(stream.CanRead);
    }

    [Fact]
    public void AnOwnedStreamIsDisposedWhenTheConstructorFails()
    {
        // a stream that cannot seek has no Length
        var stream = new UnseekableStream();
        Assert.Throws<NotSupportedException>(() => new GenericStreamReader(stream));
        Assert.True(stream.Disposed);

        stream = new UnseekableStream();
        Assert.Throws<NotSupportedException>(() => new GenericStreamReader(stream, leaveOpen: true));
        Assert.False(stream.Disposed);
    }

    private sealed class UnseekableStream : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

// The readers that are classes behave the same after Dispose. The span reader owns nothing, so its Dispose does nothing.
public class DisposedReaderTests : IDisposable
{
    private readonly string _filePath = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_filePath);
    }

    private GenericReaderBase Create(string kind, byte[] data)
    {
        File.WriteAllBytes(_filePath, data);
        return kind switch
        {
            "buffer" => new GenericBufferReader(data),
            "file" => new GenericFileReader(_filePath),
            "stream" => new GenericStreamReader(new MemoryStream(data)),
            // the stream stays usable, the reader does not
            "stream, leaveOpen" => new GenericStreamReader(new MemoryStream(data), leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [Theory]
    [InlineData("buffer")]
    [InlineData("file")]
    [InlineData("stream")]
    [InlineData("stream, leaveOpen")]
    public void EveryReadAndPositionThrowsAfterDispose(string kind)
    {
        GenericReaderBase reader = Create(kind, [.. TestBytes.FString("abc", false), .. new byte[8]]);
        Assert.Equal("abc", reader.ReadFString());
        reader.Dispose();
        reader.Dispose(); // a second Dispose does nothing

        Action[] actions =
        [
            () => reader.Read<int>(),
            () => reader.Read(Span<byte>.Empty),
            () => reader.ReadArray<byte>(0),
            () => reader.ReadArray<byte>(4),
            () => reader.ReadString(0, Encoding.UTF8),
            () => reader.ReadFString(),
            () => reader.ReadFStringArray(0),
            () => reader.ReadArray(0, () => 0),
            () => reader.ReadBoolean(),
            () => _ = reader.Position,
            () => _ = reader.PositionLong,
            () => reader.Position = 0,
            () => reader.SetPosition(0),
            () => reader.GetPosition<long>(),
            () => reader.Seek(0, SeekOrigin.Begin),
            () => reader.SeekLong(0),
            () => reader.Read<int, int>(0, SeekOrigin.Begin),
        ];

        foreach (Action action in actions)
            Assert.Throws<ObjectDisposedException>(action);

        // the length is still known
        Assert.Equal(16, reader.Length);
    }
}

internal static class TestBytes
{
    public static byte[] Int32(int value) => BitConverter.GetBytes(value);

    // an FString: int32 length including the terminator, positive for UTF-8 bytes, negative for UTF-16 chars
    public static byte[] FString(string value, bool unicode)
    {
        byte[] chars = (unicode ? Encoding.Unicode : Encoding.UTF8).GetBytes(value + '\0');
        int length = unicode ? -(value.Length + 1) : chars.Length;
        return [.. Int32(length), .. chars];
    }
}

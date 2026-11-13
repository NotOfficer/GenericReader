using System.Buffers;

namespace GenericReader;

// length bytes of scratch memory: the given stack buffer when it is large enough, else an array rented from the pool
// that Dispose returns.
internal ref struct TempBuffer
{
    private byte[]? _rented;

    public Span<byte> Span { get; }

    public TempBuffer(int length, Span<byte> stackBuffer)
    {
        if (length <= stackBuffer.Length)
        {
            Span = stackBuffer[..length];
        }
        else
        {
            _rented = ArrayPool<byte>.Shared.Rent(length);
            Span = _rented.AsSpan(0, length);
        }
    }

    public void Dispose()
    {
        if (_rented is null)
            return;

        ArrayPool<byte>.Shared.Return(_rented);
        _rented = null;
    }
}

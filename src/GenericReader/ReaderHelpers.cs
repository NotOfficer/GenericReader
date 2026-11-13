using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace GenericReader;

internal static class ReaderHelpers
{
    // The readers over memory address at most int.MaxValue bytes.
    public static int ToInt32Position(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, int.MaxValue);
        return (int)value;
    }

    // A position of any integer type, which has to lie in [0, maxPosition].
    public static long ToPosition<TPosition>(TPosition position, long maxPosition)
        where TPosition : IBinaryInteger<TPosition>
    {
        // saturated to 128-bit, where every out-of-range value stays out of range
        Int128 value = Int128.CreateSaturating(position);
        if (value < 0 || value > maxPosition)
            ThrowHelper.ThrowPositionOutOfRange(value, maxPosition);

        return (long)value;
    }

    // The position that Seek moves to, which has to lie in [0, maxPosition].
    public static long GetSeekTarget<TOffset>(TOffset offset, SeekOrigin origin, long position, long length, long maxPosition)
        where TOffset : IBinaryInteger<TOffset>
    {
        long start = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => position,
            SeekOrigin.End => length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };

        // 128-bit, so that neither a huge offset nor the addition can wrap around into the valid range
        Int128 target = start + Int128.CreateSaturating(offset);
        if (target < 0 || target > maxPosition)
            ThrowHelper.ThrowSeekOutOfRange(target, maxPosition);

        return (long)target;
    }

    // A getter array is allocated before any element is read, so its size is limited by maxByteCount instead of the data.
    public static void ValidateGetterArrayLength<T>(int length, long maxByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        // the size of a reference for reference types
        long byteCount = (long)length * Unsafe.SizeOf<T>();
        if (byteCount > maxByteCount)
            ThrowHelper.ThrowArrayTooLarge(length, byteCount, maxByteCount);
        ValidateArrayLength(length);
    }

    // Even when the data holds enough bytes, no array can have more elements than this.
    public static void ValidateArrayLength(int length)
    {
        if (length > Array.MaxLength)
            ThrowHelper.ThrowArrayTooLong(length);
    }

    // The buffer for the remaining bytes of a file or stream, which has to fit in an array.
    public static byte[] AllocateBuffer(long length)
    {
        if (length > Array.MaxLength)
            throw new IOException($"The data is too large ({length} bytes) to be loaded into a buffer.");

        // every byte gets read into it
        return GC.AllocateUninitializedArray<byte>((int)Math.Max(length, 0));
    }

    // An Unreal FString is an int32 length followed by the characters and a null terminator. A positive length counts
    // UTF-8 bytes, a negative one UTF-16 chars, both including the terminator. Returns the byte count of the characters
    // and the terminator; length must not be 0.
    public static int GetFStringByteCount(int length)
    {
        if (length > 0)
            return length;

        // more chars than this do not fit in an int as bytes, and int.MinValue cannot be negated
        if (length < -(int.MaxValue / sizeof(char)))
            ThrowHelper.ThrowInvalidFStringLength(length);

        return -length * sizeof(char);
    }

    // bytes holds GetFStringByteCount(length) bytes: the characters and the terminator
    public static string DecodeFString(ReadOnlySpan<byte> bytes, int length) => length < 0
        ? Encoding.Unicode.GetString(bytes[..^sizeof(char)])
        : Encoding.UTF8.GetString(bytes.TrimEnd(byte.MinValue));
}

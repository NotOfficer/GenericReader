using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace GenericReader;

// Throws live outside the read methods so that those stay small enough to be inlined.
[StackTraceHidden]
internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowEndOfStream() => throw new EndOfStreamException();

    [DoesNotReturn]
    public static void ThrowNegativeLength(int length) =>
        throw new InvalidDataException($"The data contains a negative length ({length}).");

    [DoesNotReturn]
    public static void ThrowInvalidFStringLength(int length) =>
        throw new InvalidDataException($"The FString length {length} is out of range.");

    [DoesNotReturn]
    public static void ThrowArrayTooLarge(int length, long byteCount, long maxByteCount) =>
        throw new InvalidDataException(
            $"An array of {length} elements ({byteCount} bytes) exceeds MaxArrayAllocationSize ({maxByteCount} bytes).");

    [DoesNotReturn]
    public static void ThrowArrayTooLong(int length) =>
        throw new InvalidDataException($"The array length {length} exceeds Array.MaxLength ({Array.MaxLength}).");

    [DoesNotReturn]
    public static void ThrowPositionOutOfRange(Int128 position, long maxPosition) =>
        throw new ArgumentOutOfRangeException("position", $"The position {position} is outside of [0, {maxPosition}].");

    [DoesNotReturn]
    public static void ThrowSeekOutOfRange(Int128 target, long maxPosition) =>
        throw new ArgumentOutOfRangeException("offset", $"The position {target} is outside of [0, {maxPosition}].");
}

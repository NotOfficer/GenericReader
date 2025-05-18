using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace GenericReader;

public abstract class GenericReaderBase : IGenericReader
{
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

	public abstract T Read<T>() where T : struct;
	public T Read<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where T : struct where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return Read<T>();
	}

	public abstract void Read<T>(Span<T> dest) where T : struct;
	public void Read<T, TOffset>(Span<T> dest, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where T : struct where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		Read(dest);
	}

	public bool ReadBoolean()
	{
		uint num = Read<uint>();
		Debug.Assert(num is 0 or 1);
		return num != 0;
	}
	public bool ReadBoolean<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadBoolean();
	}
	public string ReadString(Encoding enc)
	{
		int length = Read<int>();
		return ReadString(length, enc);
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
	public string[] ReadFStringArray()
	{
		int length = Read<int>();
		return ReadFStringArray(length);
	}

	public string[] ReadFStringArray<TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadFStringArray();
	}

	public string[] ReadFStringArray(int length)
	{
		if (length == 0)
			return [];

		string[] result = GC.AllocateUninitializedArray<string>(length);

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

	public T[] ReadArray<T>() where T : struct
	{
		int length = Read<int>();
		return ReadArray<T>(length);
	}
	public T[] ReadArray<T, TOffset>(TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where T : struct
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadArray<T>();
	}

	public abstract T[] ReadArray<T>(int length) where T : struct;
	public T[] ReadArray<T, TOffset>(int length, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where T : struct
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadArray<T>(length);
	}

	public T[] ReadArray<T>(Func<T> getter)
	{
		int length = Read<int>();
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T, TOffset>(Func<T> getter, TOffset offset, SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		int length = Read<int>();
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T>(Func<IGenericReader, T> getter)
	{
		int length = Read<int>();
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T, TOffset>(Func<IGenericReader, T> getter, TOffset offset,
		SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		int length = Read<int>();
		return ReadArray(length, getter);
	}

	public T[] ReadArray<T>(int length, Func<T> getter)
	{
		if (length == 0)
			return [];

		var result = GC.AllocateUninitializedArray<T>(length);

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
		if (length == 0)
			return [];

		var result = GC.AllocateUninitializedArray<T>(length);

		for (int i = 0; i < length; i++)
			result[i] = getter(this);

		return result;
	}

	public T[] ReadArray<T, TOffset>(int length, Func<IGenericReader, T> getter, TOffset offset,
		SeekOrigin origin = SeekOrigin.Current)
		where TOffset : IBinaryInteger<TOffset>
	{
		SeekVoid(offset, origin);
		return ReadArray(length, getter);
	}

	public abstract void Dispose();
}

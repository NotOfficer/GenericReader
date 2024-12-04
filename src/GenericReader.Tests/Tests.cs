using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GenericReader.Tests;

#if NET9_0_OR_GREATER
public class SpanReaderTests : IClassFixture<BinaryDataGenerator<GenericSpanReader>>
{
	private readonly BinaryDataGenerator<GenericSpanReader> _gen;

	public SpanReaderTests(BinaryDataGenerator<GenericSpanReader> gen)
	{
		_gen = gen;
	}

	[Fact]
	public void ReadAll()
	{
		var reader = new GenericSpanReader(_gen.Data);
		_gen.Test(ref reader);
	}
}
#endif

public class BufferReaderTests : IClassFixture<BinaryDataGenerator<GenericBufferReader>>
{
	private readonly BinaryDataGenerator<GenericBufferReader> _gen;

	public BufferReaderTests(BinaryDataGenerator<GenericBufferReader> gen)
	{
		_gen = gen;
	}

	[Fact]
	public void ReadAll()
	{
		var reader = new GenericBufferReader(_gen.Data);
		_gen.Test(ref reader);
	}
}

public class StreamReaderTests : IClassFixture<BinaryDataGenerator<GenericStreamReader>>
{
	private readonly BinaryDataGenerator<GenericStreamReader> _gen;

	public StreamReaderTests(BinaryDataGenerator<GenericStreamReader> gen)
	{
		_gen = gen;
	}

	[Fact]
	public void ReadAll()
	{
		var reader = new GenericStreamReader(new MemoryStream(_gen.Data));
		_gen.Test(ref reader);
	}
}

public class FileReaderTests : IClassFixture<BinaryDataGenerator<GenericFileReader>>, IDisposable
{
	private readonly BinaryDataGenerator<GenericFileReader> _gen;
	private readonly string _filePath;

	public FileReaderTests(BinaryDataGenerator<GenericFileReader> gen)
	{
		_gen = gen;
		_filePath = Path.GetTempFileName();
		File.WriteAllBytes(_filePath, _gen.Data);
	}

	[Fact]
	public void ReadAll()
	{
		var reader = new GenericFileReader(_filePath);

		try
		{
			_gen.Test(ref reader);
		}
		finally
		{
			reader.Dispose();
		}
	}

	public void Dispose()
	{
		File.Delete(_filePath);
	}
}

public class BinaryDataGenerator<TReader> where TReader : IGenericReader
#if NET9_0_OR_GREATER
	, allows ref struct
#endif
{
	public byte[] Data { get; }

	private readonly BinaryDataValues<TReader> _values;

	public BinaryDataGenerator()
	{
		_values = new BinaryDataValues<TReader>();

		var ms = new MemoryStream();

		{
			using var bw = new BinaryWriter(ms, Encoding.UTF8, true);

			foreach (var writeOperation in _values.WriteOperations)
			{
				writeOperation(bw);
			}
		}

		Data = ms.ToArray();
	}

	public void Test(ref TReader reader)
	{
		foreach (var verifyOperation in _values.VerifyOperations)
		{
			Assert.True(verifyOperation(ref reader));
		}

		Assert.Equal(reader.Position, reader.Length);
	}
}

public class BinaryDataValues<TReader> where TReader : IGenericReader
#if NET9_0_OR_GREATER
	, allows ref struct
#endif
{
	public delegate bool VerifyFunc(ref TReader reader);

	public List<object> Values { get; }
	public List<VerifyFunc> VerifyOperations { get; }
	public List<Action<BinaryWriter>> WriteOperations { get; }

	public BinaryDataValues()
	{
		Values = [];
		VerifyOperations = [];
		WriteOperations = [];

		Add<sbyte>();
		Add<byte>();
		Add<short>();
		Add<ushort>();
		Add<int>();
		Add<uint>();
		Add<long>();
		Add<ulong>();
		Add<nint>();
		Add<nuint>();
		Add<Int128>();
		Add<UInt128>();
		Add<float>();
		Add<double>();
		Add<decimal>();
		AddString(Encoding.UTF8, string.Empty);
		AddString(Encoding.UTF8, Helpers.GetString());
		AddString(Encoding.UTF8, Helpers.GetString(128));
		AddString(Encoding.Unicode, Helpers.GetString());
		AddString(Encoding.Unicode, Helpers.GetString(128));
		AddString(Encoding.ASCII, Helpers.GetString());
		AddString(Encoding.ASCII, Helpers.GetString(128));
		AddFString(false, string.Empty);
		AddFString(false, string.Empty, nullTerminated: false);
		AddFString(false, Helpers.GetString());
		AddFString(false, Helpers.GetString(), nullTerminated: false);
		AddFString(false, Helpers.GetString(128));
		AddFString(false, Helpers.GetString(128), nullTerminated: false);
		AddFString(true, string.Empty);
		AddFString(true, Helpers.GetString());
		AddFString(true, Helpers.GetString(128));
		for (var i = 0; i < 32; i++)
		{
			Add<sbyte>();
			Add<byte>();
			Add<short>();
			Add<ushort>();
			Add<int>();
			Add<uint>();
			Add<long>();
			Add<ulong>();
			Add<nint>();
			Add<nuint>();
			Add<Int128>();
			Add<UInt128>();
			Add<float>();
			Add<double>();
			Add<decimal>();
			AddFString(false);
			AddFString(false, nullTerminated: false);
			AddFString(true);
			AddString(Encoding.UTF8);
			AddString(Encoding.ASCII);
			AddString(Encoding.Unicode);
		}
	}

	private void Add<TValue>()
		where TValue : struct, IEquatable<TValue>
	{
		Unsafe.SkipInit(out TValue value);
		Helpers.Fill(ref value);
		Values.Add(value);
		VerifyOperations.Add((ref TReader reader) =>
		{
			var readValue = reader.Read<TValue>();
			return readValue.Equals(value);
		});
		WriteOperations.Add(writer =>
		{
			writer.Write(Helpers.AsBytesSpan(ref value));
		});
	}

	private void AddString(Encoding encoding, string? value = null)
	{
		value ??= Helpers.GetName();
		Values.Add(value);
		VerifyOperations.Add((ref TReader reader) =>
		{
			var readValue = reader.ReadString(encoding);
			return readValue.Equals(value, StringComparison.Ordinal);
		});
		WriteOperations.Add(writer =>
		{
			var numBytes = encoding.GetMaxByteCount(value.Length);
			Span<byte> bytes = stackalloc byte[numBytes];
			var written = encoding.GetBytes(value, bytes);
			writer.Write(written);
			writer.Write(bytes.Slice(0, written));
		});
	}

	private void AddFString(bool unicode, string? value = null, bool nullTerminated = true)
	{
		value ??= Helpers.GetName();
		Values.Add(value);
		VerifyOperations.Add((ref TReader reader) =>
		{
			var readValue = reader.ReadFString();
			return readValue.Equals(value, StringComparison.Ordinal);
		});
		WriteOperations.Add(writer =>
		{
			var encoding = unicode ? Encoding.Unicode : Encoding.UTF8;
			var numBytes = encoding.GetMaxByteCount(value.Length);
			Span<byte> bytes = stackalloc byte[numBytes];
			var written = encoding.GetBytes(value, bytes);
			writer.Write(unicode ? -(value.Length + 1) : written + (nullTerminated ? 1 : 0));
			writer.Write(bytes.Slice(0, written));
			if (!nullTerminated) return;
			for (var i = 0; i < (unicode ? sizeof(char) : sizeof(byte)); i++)
				writer.Write(byte.MinValue);
		});
	}
}

public static class Helpers
{
	private static readonly string[] Names =
	[
		"Alice Johnson", "Michael Smith", "Emma Brown", "James Williams", "Sophia Martinez",
		"Benjamin Taylor", "Olivia Wilson", "William Davis", "Ava Thompson", "Mason White",
		"Isabella Harris", "Liam Lewis", "Mia Walker", "Noah Clark", "Emily Hall",
		"Ethan Allen", "Grace Young", "Lucas King", "Chloe Wright", "Logan Scott",
		"Ella Green", "Jacob Baker", "Amelia Adams", "Alexander Nelson", "Hannah Hill",
		"Henry Rivera", "Abigail Carter", "Daniel Roberts", "Evelyn Stewart", "Jackson Perez",
		"Victoria Sanders", "Matthew Morales", "Zoe Reed", "Samuel Cooper", "Lily Ward",
		"David Torres", "Ellie Phillips", "Joseph Parker", "Layla Bennett", "Caleb Jenkins",
		"Lucy Perry", "Joshua Powell", "Scarlett Long", "Andrew Patterson", "Madison Hughes",
		"Nathan Price", "Aubrey Butler", "Ryan Barnes", "Penelope Ross", "Dylan Henderson",
		"Mila Coleman", "Gabriel Brooks", "Nora Howard", "Isaac Gray", "Addison Griffin",
		"Oliver Bryant", "Savannah Campbell", "Levi Murphy", "Stella Foster", "Sebastian Simmons"
	];

	public static string GetName() => Names[RandomNumberGenerator.GetInt32(Names.Length)];

	public static string GetString(int length) => RandomNumberGenerator.GetHexString(length);
	public static string GetString() => GetString(RandomNumberGenerator.GetInt32(12, 64));

	public static Span<byte> AsBytesSpan<T>(ref T value) where T : struct
	{
		return MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref value), Unsafe.SizeOf<T>());
	}

	public static void Fill<T>(ref T value) where T : struct
	{
		RandomNumberGenerator.Fill(AsBytesSpan(ref value));
	}
}

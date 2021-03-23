using System;
using System.IO;
using System.Text;

namespace GenericReader
{
	public interface IGenericReader : IDisposable
	{
		public long Position { get; set; }
		public int Size { get; }
		public int Seek(int offset, SeekOrigin origin);
		public T Read<T>() where T : unmanaged;
		public T Read<T>(int offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged;
		public bool ReadBoolean();
		public bool ReadBoolean(int offset, SeekOrigin origin = SeekOrigin.Current);
		public byte ReadByte();
		public byte ReadByte(int offset, SeekOrigin origin = SeekOrigin.Current);
		public byte[] ReadBytes(int length);
		public byte[] ReadBytes(int length, int offset, SeekOrigin origin = SeekOrigin.Current);
		public string ReadString(Encoding enc);
		public string ReadString(Encoding enc, int offset, SeekOrigin origin = SeekOrigin.Current);
		public string ReadString(int length, Encoding enc);
		public string ReadString(int length, Encoding enc, int offset, SeekOrigin origin = SeekOrigin.Current);
		public string ReadFString();
		public string ReadFString(int offset, SeekOrigin origin = SeekOrigin.Current);
		public string[] ReadFStringArray();
		public string[] ReadFStringArray(int offset, SeekOrigin origin);
		public string[] ReadFStringArray(int length);
		public string[] ReadFStringArray(int length, int offset, SeekOrigin origin = SeekOrigin.Current);
		public T[] ReadArray<T>() where T : unmanaged;
		public T[] ReadArray<T>(int offset, SeekOrigin origin) where T : unmanaged;
		public T[] ReadArray<T>(int length) where T : unmanaged;
		public T[] ReadArray<T>(int length, int offset, SeekOrigin origin = SeekOrigin.Current) where T : unmanaged;
		public T[] ReadArray<T>(Func<T> getter);
		public T[] ReadArray<T>(Func<T> getter, int offset, SeekOrigin origin = SeekOrigin.Current);
		public T[] ReadArray<T>(Func<IGenericReader, T> getter);
		public T[] ReadArray<T>(Func<IGenericReader, T> getter, int offset, SeekOrigin origin = SeekOrigin.Current);
		public T[] ReadArray<T>(int length, Func<T> getter);
		public T[] ReadArray<T>(int length, Func<T> getter, int offset, SeekOrigin origin = SeekOrigin.Current);
		public T[] ReadArray<T>(int length, Func<IGenericReader, T> getter);
		public T[] ReadArray<T>(int length, Func<IGenericReader, T> getter, int offset, SeekOrigin origin = SeekOrigin.Current);
	}
}
﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Voron.Impl;
using Voron.Trees;
using Voron.Util.Conversion;

namespace Voron
{
	public unsafe class Slice
	{
		public static Slice AfterAllKeys = new Slice(SliceOptions.AfterAllKeys);
		public static Slice BeforeAllKeys = new Slice(SliceOptions.BeforeAllKeys);
		public static Slice Empty = new Slice(new byte[0]);

		private ushort _size;
		public SliceOptions Options;
		private readonly byte[] _array;
		private byte* _pointer;

		public ushort Size
		{
			get { return _size; }
		}

		public void Set(byte* p, ushort size)
		{
			_pointer = p;
			_size = size;
		}

		public Slice(SliceOptions options)
		{
			Options = options;
			_pointer = null;
			_array = null;
			_size = 0;
		}

		public Slice(byte* key, ushort size)
		{
			_size = size;
			Options = SliceOptions.Key;
			_array = null;
			_pointer = key;
		}

		public Slice(byte[] key) : this(key, (ushort)key.Length)
		{
			
		}

		public Slice(byte[] key, ushort size)
		{
			if (key == null) throw new ArgumentNullException("key");
			_size = size;
			Options = SliceOptions.Key;
			_pointer = null;
			_array = key;
		}

		public Slice(NodeHeader* node)
		{
			Options = SliceOptions.Key;
			Set(node);
		}

		protected bool Equals(Slice other)
		{
			return Compare(other, NativeMethods.memcmp) == 0;
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != GetType()) return false;
			return Equals((Slice)obj);
		}

		public override int GetHashCode()
		{
			if (_array != null)
				return ComputeHashArray();
			return ComputeHashPointer();
		}

		private int ComputeHashPointer()
		{
			unchecked
			{
				const int p = 16777619;
				int hash = (int)2166136261;

				for (int i = 0; i < _size; i++)
					hash = (hash ^ _pointer[i]) * p;

				hash += hash << 13;
				hash ^= hash >> 7;
				hash += hash << 3;
				hash ^= hash >> 17;
				hash += hash << 5;
				return hash;
			}
		}

		private int ComputeHashArray()
		{
			unchecked
			{
				const int p = 16777619;
				int hash = (int)2166136261;

				for (int i = 0; i < _size; i++)
					hash = (hash ^ _array[i]) * p;

				hash += hash << 13;
				hash ^= hash >> 7;
				hash += hash << 3;
				hash ^= hash >> 17;
				hash += hash << 5;
				return hash;
			}
		}

		public override string ToString()
		{
			// this is used for debug purposes only
			if (Options != SliceOptions.Key)
				return Options.ToString();

			if (_array != null)
				return Encoding.UTF8.GetString(_array,0, _size);

			return new string((sbyte*)_pointer, 0, _size, Encoding.UTF8);
		}

		public int Compare(Slice other, SliceComparer cmp)
		{
			Debug.Assert(Options == SliceOptions.Key);
			Debug.Assert(other.Options == SliceOptions.Key);

			var r = CompareData(other, cmp, Math.Min(Size, other.Size));
			if (r != 0)
				return r;
			return Size - other.Size;
		}

		public bool StartsWith(Slice other, SliceComparer cmp)
		{
			if (Size < other.Size)
				return false;
			return CompareData(other, cmp, other.Size) == 0;
		}

		private int CompareData(Slice other, SliceComparer cmp, ushort size)
		{
			if (_array != null)
			{
				fixed (byte* a = _array)
				{
					if (other._array != null)
					{
						fixed (byte* b = other._array)
						{
							return cmp(a, b, size);
						}
					}
					return cmp(a, other._pointer, size);
				}
			}
			if (other._array != null)
			{
				fixed (byte* b = other._array)
				{
					return cmp(_pointer, b, size);
				}
			}
			return cmp(_pointer, other._pointer, size);
		}

		public static implicit operator Slice(string s)
		{
			return new Slice(Encoding.UTF8.GetBytes(s));
		}

		public void CopyTo(byte* dest)
		{
			if (_array == null)
			{
				NativeMethods.memcpy(dest, _pointer, _size);
				return;
			}
			fixed (byte* a = _array)
			{
				NativeMethods.memcpy(dest, a, _size);
			}
		}

		public void CopyTo(byte[] dest)
		{
			if (_array == null)
			{
				fixed (byte* p = dest)
					NativeMethods.memcpy(p, _pointer, _size);
				return;
			}
			Buffer.BlockCopy(_array, 0, dest, 0, _size);
		}

		public void Set(NodeHeader* node)
		{
			Set((byte*)node + Constants.NodeHeaderSize, node->KeySize);
		}


		public Slice Clone()
		{
			var buffer = new byte[Size];
			if (_array == null)
			{
				fixed (byte* dest = buffer)
				{
					NativeMethods.memcpy(dest, _pointer, _size);
				}
			}
			else
			{
				Buffer.BlockCopy(_array, 0, buffer, 0, Size);
			}
			return new Slice(buffer);
		}

		public long ToInt64()
		{
			if (Size != sizeof(long))
				throw new NotSupportedException("Invalid size for int 64 key");
			if (_array != null)
				return EndianBitConverter.Big.ToInt64(_array, 0);

			var buffer = new byte[Size];
			fixed (byte* dest = buffer)
			{
				NativeMethods.memcpy(dest, _pointer, _size);
			}

			return EndianBitConverter.Big.ToInt64(buffer, 0);
		}
	}
}
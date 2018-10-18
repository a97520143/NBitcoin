﻿using System;
using System.Collections;

namespace NBitcoin
{
	/// <summary> Provides a view of an array of bits as a stream of bits. </summary>
	class BitStream
	{
		private byte[] _buffer;
		private int _writePos;
		private int _readPos;

		public BitStream()
			: this(new byte[8 * 1024])
		{
		}

		public BitStream(byte[] buffer)
		{
			var newBuffer = new byte[buffer.Length];
			Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
			_buffer = newBuffer;
			_writePos = 0;
			_readPos = 0;
		}

		public void WriteBit(bool bit)
		{
			EnsureCapacity();
			if (bit)
			{
				_buffer[_writePos / 8] |= (byte)(1 << ( 8 - (_writePos % 8) - 1));
			}
			_writePos++;
		}

        public void WriteBits(ulong data, byte count)
        {
			data <<= (64 - count);
			while(count >= 8)
			{
				var b = (byte)(data >> (64 - 8));
				WriteByte(b);
				data <<= 8;
				count -= 8;
			}

			while(count > 0)
			{
				var bit = data >> (64 - 1);
				WriteBit(bit == 1);
				data <<= 1;
				count--;
			}
		}

		public void WriteByte(byte b)
		{
			EnsureCapacity();

			var remainCount = (_writePos % 8);
			var i = _writePos / 8;
			_buffer[i] |= (byte)(b >> remainCount);

			if(remainCount > 0)
			{
				EnsureCapacity();
				
				_buffer[i+1] = (byte)(b << (8 - remainCount));
			}
			_writePos+=8;
		}

		public bool TryReadBit(out bool bit)
		{
			bit = false;
			var i = _readPos / 8;
			if ( i == _buffer.Length)
			{
				return false;
			}

			var mask = 1 << (8 - (_readPos % 8) - 1); 

			bit = (_buffer[i] & mask) == mask;
			_readPos++;
			return true;
		}


		public bool TryReadBits(int count, out ulong bits)
		{
			var i = (_readPos + count) / 8;
			if ( i >= _buffer.Length)
			{
				bits = 0U;
				return false;
			}

			var val = 0UL;
			while(count >= 8)
			{
				val <<= 8;
				TryReadByte(out var readedByte);
				val |= (ulong)readedByte;
				count -= 8;
			}

			while(count > 0)
			{
				val <<= 1;
				var	bit = false;
				var ii = _readPos / 8;

				var mask = 1 << (8 - (_readPos % 8) - 1); 

				bit = (_buffer[ii] & mask) == mask;
				_readPos++;
				val |= bit ? 1UL : 0UL;
				count--;
			}
			bits = val;
			return true;
		}

		public bool TryReadByte(out byte b)
		{
			b = 0;
			var i = _readPos / 8;
			if ( i == _buffer.Length)
			{
				return false;
			}

			var remainCount = _readPos % 8;
			b = (byte)(_buffer[i] << remainCount);

			if(remainCount > 0)
			{
				if(i+1 == _buffer.Length)
				{
					b = 0;
					return false;
				}
				b |= (byte)(_buffer[i+1] >> (8 - remainCount));
			}
			_readPos += 8;
			return true;
		}

		public byte[] ToByteArray()
		{
			var arraySize = (_writePos + 7) / 8;
			var byteArray = new byte[arraySize];
			Array.Copy(_buffer, byteArray, arraySize);
			return byteArray;
		}

		private void EnsureCapacity()
		{
			if ( (_writePos / 8) == (_buffer.Length - 1))
			{
				Array.Resize(ref _buffer, _buffer.Length + ( 4 * 1024 ));
			}
		}
	}


	internal class GRCodedStreamWriter
	{
		private readonly BitStream _stream;
		private readonly byte _p;
		private readonly ulong _modP;
		private ulong _lastValue;

		public GRCodedStreamWriter(BitStream stream, byte p)
		{
			_stream = stream;
			_p = p;
			_modP = (1UL << p);
			_lastValue = 0UL;
		}

		public void Write(ulong value)
		{
			var diff = value - _lastValue;

			var remainder = diff & (_modP - 1);
			var quotient = (diff - remainder) >> _p;

			while (quotient > 0)
			{
				_stream.WriteBit(true);
				quotient--;
			}
			_stream.WriteBit(false);
			_stream.WriteBits(remainder, _p);
			_lastValue = value;
		}
	}

	internal class GRCodedStreamReader
	{
		private readonly BitStream _stream;
		private readonly byte _p;
		private readonly ulong _modP;
		private ulong _lastValue;

		public GRCodedStreamReader(BitStream stream, byte p, ulong lastValue)
		{
			_stream = stream;
			_p = p;
			_modP = (1UL << p);
			_lastValue = lastValue;
		}

		public bool TryRead(out ulong value)
		{
			if(TryReadUInt64(out var readedValue)){
				var currentValue = _lastValue + readedValue;
				_lastValue = currentValue;
				value = currentValue;
				return true;
			}

			value = 0;
			return false;
		}

		private bool TryReadUInt64(out ulong value)
		{
			value = 0U;
			var count = 0UL;
			if(!_stream.TryReadBit(out var bit))
				return false;

			while (bit)
			{
				count++;
				if(!_stream.TryReadBit(out bit))
					return false;
			}

			if(_stream.TryReadBits(_p, out var remainder))
			{
				value = (count * _modP) + remainder;
				return true;
			}

			return false;
		}
	}
}
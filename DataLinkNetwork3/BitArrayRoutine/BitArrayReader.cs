using System;
using System.Collections;

namespace DataLinkNetwork3.BitArrayRoutine
{
    public class BitArrayReader
    {
        public BitArray Array { get; }
        public int Position { get; private set; }

        public BitArrayReader(BitArray array, int position = 0)
        {
            Array = array;
            Position = position;
        }

        public void Adjust(int shift)
        {
            Position += shift;
        }

        public BitArray Read(int size)
        {
            if (Position + size > Array.Length)
            {
                throw new ArgumentException($"Attempt to read {size}, when only {Array.Length - Position} available");
            }

            BitArray result = new BitArray(size);

            for (int i = 0; i < size; i++)
            {
                result[i] = Array[Position++];
            }

            return result;
        }
    }
}
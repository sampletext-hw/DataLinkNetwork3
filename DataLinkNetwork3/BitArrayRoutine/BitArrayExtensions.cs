using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace DataLinkNetwork2.BitArrayRoutine
{
    public static class BitArrayExtensions
    {
        public static string ToBinString(this BitArray bitArray)
        {
            StringBuilder builder = new StringBuilder(bitArray.Length);
            int pos = 1;
            for (var i = 0; i < bitArray.Count; i++, pos++)
            {
                builder.Append(bitArray[i] ? 1 : 0);
                if (pos % 8 == 0)
                {
                    builder.Append(" ");
                }
            }

            return builder.ToString();
        }

        public static bool IsSameNoCopy(this BitArray srcArray, BitArray cmpArray, int srcStartIndex, int cmpStartIndex, int length)
        {
            for (var i = 0; i < length; i++)
            {
                if (srcArray[srcStartIndex + i] != cmpArray[cmpStartIndex + i])
                {
                    return false;
                }
            }

            return true;
        }

        public static int FindFlag(this BitArray bitArray, int offset = 0)
        {
            int i = offset;
            while (i <= bitArray.Length - C.FlagSize)
            {
                if (bitArray.IsSameNoCopy(Frame.Flag, i, 0, C.FlagSize))
                {
                    // We hit the flag
                    return i;
                }

                i++;
            }

            return -1;
        }

        public static byte[] ToByteArray(this BitArray data)
        {
            byte[] array = new byte[data.Length / 8 + (data.Length % 8 > 0 ? 1 : 0)];
            data.CopyTo(array, 0);
            return array;
        }

        public static BitArray BitStaff(this BitArray data)
        {
            int ones = 0;
            int extraBits = 0;
            for (var i = 0; i < data.Count; i++)
            {
                if (data[i])
                {
                    ones++;
                }

                if (ones == 5)
                {
                    extraBits++;
                    ones = 0;
                }
            }

            ones = 0;
            if (extraBits > 0)
            {
                BitArray result = new BitArray(data.Length + extraBits);
                int position = 0;
                for (var i = 0; i < data.Length; i++)
                {
                    if (data[i])
                    {
                        ones++;
                    }
                    else
                    {
                        ones = 0;
                    }

                    if (ones == 5)
                    {
                        result[position++] = false;
                        ones = 0;
                    }

                    result[position++] = data[i];
                }

                return result;
            }

            return data;
        }

        public static BitArray DeBitStaff(this BitArray data)
        {
            int ones = 0;
            int extraBits = 0;
            for (var i = 0; i < data.Count; i++)
            {
                if (data[i])
                {
                    ones++;
                }
                else
                {
                    ones = 0;
                }

                if (ones == 5)
                {
                    extraBits++;
                    ones = 0;
                }
            }

            ones = 0;
            if (extraBits > 0)
            {
                BitArray result = new BitArray(data.Length - extraBits);

                int position = 0;
                for (var i = 0; i < data.Length; i++)
                {
                    if (data[i])
                    {
                        ones++;
                    }

                    if (ones == 5)
                    {
                        position++;
                        ones = 0;
                    }

                    result[position++] = data[i];
                }

                return result;
            }

            return data;
        }

        public static List<BitArray> Split(this BitArray array, int maxSize)
        {
            List<BitArray> parts = new();

            BitArrayReader reader = new BitArrayReader(array);

            while (reader.Position + maxSize <= array.Length)
            {
                parts.Add(reader.Read(maxSize));
            }

            if (reader.Position < array.Length)
            {
                parts.Add(reader.Read(array.Length - reader.Position));
            }

            return parts;
        }

        public static BitArrayReader Reader(this BitArray bitArray)
        {
            return new(bitArray);
        }
        
        public static BitArrayWriter Writer(this BitArray bitArray)
        {
            return new(bitArray);
        }
    }
}
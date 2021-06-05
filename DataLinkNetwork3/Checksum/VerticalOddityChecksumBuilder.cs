using System.Collections;
using System.Linq;
using DataLinkNetwork2.Abstractions;
using DataLinkNetwork2.BitArrayRoutine;

namespace DataLinkNetwork2.Checksum
{
    public class VerticalOddityChecksumBuilder : IChecksumBuilder
    {
        public BitArray Build(BitArray data)
        {
            BitArray result = new BitArray(C.ChecksumSize); // 2 bytes, one for rows and 1 for cols

            // Arrays for row sums and col sums
            int[] rowSums = new int[8];
            int[] colSums = new int[8];

            // calculate sums for the matrix
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    int index = i * 8 + j;
                    if (index < data.Length)
                    {
                        rowSums[i] += data[index] ? 1 : 0;
                        colSums[j] += data[index] ? 1 : 0;
                    }
                }
            }

            // We are interested only in remainder
            for (int i = 0; i < 8; i++)
            {
                rowSums[i] %= 2;
                colSums[i] %= 2;
            }

            // Write everything
            var writer = new BitArrayWriter(result);
            writer.Write(new BitArray(rowSums.Select(t => t > 0).ToArray()));
            writer.Write(new BitArray(colSums.Select(t => t > 0).ToArray()));
            return result;
        }
    }
}
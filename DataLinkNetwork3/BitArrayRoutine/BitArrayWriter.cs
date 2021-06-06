using System.Collections;

namespace DataLinkNetwork3.BitArrayRoutine
{
    public class BitArrayWriter
    {
        public BitArray Array { get; }

        public int Position { get; private set; }
        
        public BitArrayWriter(BitArray array, int position = 0)
        {
            Array = array;
            Position = position;
        }

        public void Write(BitArray data)
        {
            for (var i = 0; i < data.Length; i++)
            {
                Array[Position++] = data[i];
            }
        }
    }
}
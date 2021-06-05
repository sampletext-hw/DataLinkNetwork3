using System.Collections;

namespace DataLinkNetwork2.Abstractions
{
    public interface IChecksumBuilder
    {
        public BitArray Build(BitArray data);
    }
}
using System.Collections;

namespace DataLinkNetwork3.Abstractions
{
    public interface IChecksumBuilder
    {
        public BitArray Build(BitArray data);
    }
}
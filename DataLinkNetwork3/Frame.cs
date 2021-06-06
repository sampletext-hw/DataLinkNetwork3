using System;
using System.Collections;
using DataLinkNetwork3.BitArrayRoutine;
using DataLinkNetwork3.Checksum;

namespace DataLinkNetwork3
{
    public class Frame
    {
        public static readonly BitArray Flag = new(new[] {false, true, true, true, true, true, true, false});

        public BitArray Data { get; set; }

        public BitArray Address { get; set; }

        public BitArray Control { get; set; }

        public BitArray Checksum { get; set; }

        public Frame(BitArray data, BitArray address, BitArray control)
        {
            if (data.Length > C.MaxFrameDataSize)
            {
                throw new ArgumentException(
                    $"Data length can't exceed {C.MaxFrameDataSize}, actual {data.Length}");
            }

            if (address.Length > C.AddressSize)
            {
                throw new ArgumentException(
                    $"Address length can't exceed {C.AddressSize}, actual {address.Length}");
            }

            if (control.Length > C.ControlSize)
            {
                throw new ArgumentException(
                    $"Control length can't exceed {C.ControlSize}, actual {control.Length}");
            }

            Data = data;
            Address = address;
            Control = control;
        }

        public BitArray Build()
        {
            Checksum = new VerticalOddityChecksumBuilder().Build(Data);

            int frameSize =
                C.FlagSize +
                C.AddressSize +
                C.ControlSize +
                Data.Length +
                C.ChecksumSize +
                C.FlagSize;

            BitArray frameArray = new BitArray(frameSize);

            var writer = new BitArrayWriter(frameArray);

            writer.Write(Flag);
            writer.Write(Address);
            writer.Write(Control);
            writer.Write(Data);
            writer.Write(Checksum);
            writer.Write(Flag);

            return frameArray;
        }

        public static Frame Parse(BitArray rawBits)
        {
            var startFlagPosition = rawBits.FindFlag();
            if (startFlagPosition == -1)
            {
                throw new ArgumentException($"{nameof(rawBits)} doesn't contain start Flag");
            }

            int minimumNextFlag = rawBits.Length - C.FlagSize;

            var nextFlagPosition = rawBits.FindFlag(minimumNextFlag);

            if (nextFlagPosition == -1)
            {
                throw new ArgumentException($"{nameof(rawBits)} doesn't contain second Flag");
            }

            BitArrayReader reader = new BitArrayReader(rawBits, startFlagPosition);

            reader.Read(C.FlagSize);
            var addressBits = reader.Read(C.AddressSize);
            var controlBits = reader.Read(C.ControlSize);
            var currentReaderPosition = reader.Position;

            reader.Adjust(nextFlagPosition - currentReaderPosition - C.ChecksumSize);
            var checksumStartPosition = reader.Position;
            var checksumBits = reader.Read(C.ChecksumSize);
            reader.Adjust(-C.ChecksumSize - checksumStartPosition + currentReaderPosition);
            var dataBits = reader.Read(checksumStartPosition - currentReaderPosition);
            
            return new Frame(dataBits, addressBits, controlBits) {Checksum = checksumBits};
        }

        public override string ToString()
        {
            return
                $"Frame {{\n  {nameof(Data)}: {Data.ToBinString()},\n  {nameof(Address)}: {Address.ToBinString()},\n  {nameof(Control)}: {Control.ToBinString()},\n  {nameof(Checksum)}: {Checksum.ToBinString()}\n}}";
        }
    }
}
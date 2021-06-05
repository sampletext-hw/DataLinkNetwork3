using System;
using DataLinkNetwork2.Communication;

namespace DataLinkNetwork2.Abstractions
{
    public interface ISocket
    {
        public event Action Connected;
        public event Action Disconnected;

        public event Action<byte[]> Received;

        public event Action StartedSending;
        public event Action StartedReceiving;
        
        void Send(byte[] array);

        void Connect(ISocket socket);

        void Disconnect();

        (MiddlewareBuffer, MiddlewareBuffer) AcceptConnect(ISocket socket);

        void AcceptDisconnect();
    }
}
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace DataLinkNetwork3.Communication
{
    public class MiddlewareBuffer
    {
        private readonly Queue<BitArray> _dataQueue;
        private int _lastReceiveStatusCode;

        private readonly Mutex _acquireMutex;

        public MiddlewareBuffer()
        {
            _acquireMutex = new(false);
            _dataQueue = new Queue<BitArray>();
        }

        public bool HasAvailable()
        {
            return _dataQueue.Count > 0;
        }

        public void Acquire()
        {
            _acquireMutex.WaitOne();
        }
        
        public void Release()
        {
            _acquireMutex.ReleaseMutex();
        }

        public BitArray Get()
        {
            var bitArray = _dataQueue.Dequeue();
            return bitArray;
        }
        
        public void Push(BitArray data)
        {
            _dataQueue.Enqueue(data);
        }

        public void SetStatusCode(int statusCode)
        {
            _lastReceiveStatusCode = statusCode;
        }
        
        public int GetStatusCode()
        {
            return _lastReceiveStatusCode;
        }

        public void ResetStatus()
        {
            _lastReceiveStatusCode = 0;
        }
    }
}
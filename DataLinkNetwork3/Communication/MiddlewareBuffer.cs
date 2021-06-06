using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace DataLinkNetwork3.Communication
{
    public class MiddlewareBuffer
    {
        private readonly Queue<BitArray> _dataQueue;

        private ResponseStatus _responseStatus;

        private int _srejCount = 0;

        private readonly Mutex _acquireMutex;

        public MiddlewareBuffer()
        {
            _acquireMutex = new();
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

        public void SetResponseStatus(ResponseStatus responseStatus)
        {
            _responseStatus = responseStatus;
        }

        public void SetSrejCount(int count)
        {
            _srejCount = count;
        }
        
        public int GetSrejCount()
        {
            return _srejCount;
        }

        public ResponseStatus GetResponseStatus()
        {
            return _responseStatus;
        }

        public void ResetResponseStatus()
        {
            _responseStatus = ResponseStatus.Undefined;
        }
    }
}
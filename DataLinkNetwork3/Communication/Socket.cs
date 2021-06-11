using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DataLinkNetwork3.Abstractions;
using DataLinkNetwork3.BitArrayRoutine;
using DataLinkNetwork3.Checksum;

namespace DataLinkNetwork3.Communication
{
    public class Socket : ISocket
    {
        // Two buffers for data transmition
        private MiddlewareBuffer _sendBuffer;
        private MiddlewareBuffer _receiveBuffer;

        private string _title;

        // Paired Socket
        private ISocket _pairedSocket;

        // Flag of connection, and it's locker
        private bool _connected;
        private readonly Mutex _connectedMutex;

        // Events
        public event Action Connected;
        public event Action Disconnected;

        public event Action<byte[]> Received;

        public event Action StartedSending;
        public event Action StartedReceiving;

        // Queued packets for send
        private readonly Queue<byte[]> _sendQueue;

        // Small send barrier, so send thread wouldn't loop itself till death
        private readonly AutoResetEvent _sendBarrier;

        // 2 background routine threads
        private Thread _sendThread;
        private Thread _receiveThread;

        // Flag, indicating, whether we should terminate background tasks
        private volatile bool _terminate;

        public Socket(string title = "Socket")
        {
            _title = title;
            _connectedMutex = new();
            _sendBarrier = new(false);
            _sendQueue = new();
        }

        /// <summary>
        /// This is a send thread routine, waits and sends data from _sendQueue
        /// </summary>
        private void SendRoutine()
        {
            StartedSending?.Invoke();
            while (!_terminate)
            {
                // When all packets are sent, thread waits for new signal with AutoResetEvent
                _sendBarrier.WaitOne();
                while (_sendQueue.Count > 0)
                {
                    var result = PerformSend(_sendQueue.Dequeue());

                    // Console.WriteLine("Sent");
                }
            }
        }

        /// <summary>
        /// Single Packet Send 
        /// </summary>
        /// <param name="dataArray">Packet Data Array</param>
        private bool PerformSend(byte[] dataArray)
        {
            _sendBuffer.Acquire();
            _sendBuffer.Push(Frame.BuildFirstFrame());
            _sendBuffer.Release();

            while (AwaitResponse() != ResponseStatus.RR)
            {
                Console.WriteLine($"{_title}: Waiting for receiver");
                Thread.Sleep(10);
            }

            Console.WriteLine($"{_title}: Receiver Ready");

            _sendBuffer.ResetResponseStatus();

            // BitStaff all data and split to frame size
            var dataBitArray = new BitArray(dataArray).BitStaff();

            var arrays = dataBitArray.Split(C.MaxFrameDataSize);

            var taken = 0;

            var windowedBitArrays = new List<BitArray>();

            var srejCount = 0;

            var index = 0;

            while (taken < arrays.Count)
            {
                if (srejCount == 0)
                {
                    windowedBitArrays.Clear();
                }
                else
                {
                    while (windowedBitArrays.Count > srejCount)
                    {
                        windowedBitArrays.RemoveAt(0);
                    }

                    srejCount = 0;
                    _sendBuffer.SetSrejCount(0);
                }

                while (windowedBitArrays.Count < C.WindowSize && taken < arrays.Count)
                {
                    windowedBitArrays.Add(arrays[taken++]);
                }

                Console.WriteLine($"{_title}: Filled window");

                bool sendSuccessful = false;

                while (!sendSuccessful)
                {
                    _sendBuffer.Acquire();
                    _sendBuffer.ResetResponseStatus();
                    for (int i = 0; i < windowedBitArrays.Count; i++)
                    {
                        var dataBits = windowedBitArrays[i];

                        var addressBits = new BitArray(C.AddressSize);
                        var controlBits = new BitArray(C.ControlSize);

                        // First byte is a frame id, second is control flag
                        var controlBytes = new byte[] {(byte)(index++ % 8), 0};
                        controlBits.Writer().Write(new BitArray(controlBytes));

                        var frame = new Frame(dataBits, addressBits, controlBits);

                        var frameBits = frame.Build();

                        _sendBuffer.Push(frameBits);
                    }

                    BitArray resultingChecksum = new BitArray(C.ChecksumSize);
                    for (int i = 0; i < windowedBitArrays.Count; i++)
                    {
                        var dataBits = windowedBitArrays[i];
                        var checksumBuilder = new VerticalOddityChecksumBuilder();

                        var checksum = checksumBuilder.Build(dataBits);
                        resultingChecksum.Xor(checksum);
                    }

                    _sendBuffer.Push(Frame.BuildControlFrame(resultingChecksum));
                    _sendBuffer.Release();

                    var responseStatus = AwaitResponse();

                    switch (responseStatus)
                    {
                        case ResponseStatus.Undefined:
                        {
                            // Something went wrong
                            sendSuccessful = false;
                            break;
                        }
                        case ResponseStatus.RR:
                        {
                            sendSuccessful = true;
                            break;
                        }
                        case ResponseStatus.RNR:
                        {
                            Console.WriteLine($"{_title}: Received RNR, Waiting");
                            while (AwaitResponse() != ResponseStatus.RR)
                            {
                                Thread.Sleep(10);
                            }

                            Console.WriteLine($"{_title}: Received RR, Continuing");
                            sendSuccessful = true;
                            break;
                        }
                        case ResponseStatus.REJ:
                        {
                            sendSuccessful = false;
                            break;
                        }
                        case ResponseStatus.SREJ:
                        {
                            srejCount = _sendBuffer.GetSrejCount();
                            sendSuccessful = true;
                            break;
                        }
                        default:
                        {
                            Console.WriteLine($"{_title}: ResponseStatus Unknown");
                            break;
                        }
                    }

                    _sendBuffer.ResetResponseStatus();
                }
            }

            _sendBuffer.Acquire();
            _sendBuffer.Push(Frame.BuildEndFrame());
            _sendBuffer.Release();

            while (AwaitResponse() != ResponseStatus.RR)
            {
                Console.WriteLine($"{_title}: Waiting for end confirmation");
                Thread.Sleep(10);
            }

            Console.WriteLine($"{_title}: End confirmation received");

            return true;
        }

        /// <summary>
        /// Small utility for awaiting a status code from receiver
        /// </summary>
        /// <returns>Status code from receiver</returns>
        private ResponseStatus AwaitResponse()
        {
            var stopwatch = Stopwatch.StartNew();

            var lastReceiveStatus = ResponseStatus.Undefined;

            while (lastReceiveStatus == ResponseStatus.Undefined)
            {
                lastReceiveStatus = _sendBuffer.GetResponseStatus();

                if (lastReceiveStatus == ResponseStatus.Undefined)
                {
                    if (stopwatch.ElapsedMilliseconds > C.SendTimeoutMilliseconds)
                    {
                        stopwatch.Stop();
                        lastReceiveStatus = ResponseStatus.REJ;
                        break;
                    }

                    Thread.Sleep(10);
                }
            }

            return lastReceiveStatus;
        }

        /// <summary>
        /// Receive Thread Routine, which checks for available data and invokes a Received event
        /// </summary>
        private void ReceiveRoutine()
        {
            StartedReceiving?.Invoke();
            while (!_terminate)
            {
                // If something is available
                if (_receiveBuffer.HasAvailable())
                {
                    // Receive while available
                    while (_receiveBuffer.HasAvailable())
                    {
                        var bytes = InternalReceive();

                        Received?.Invoke(bytes);
                    }
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
        }

        /// <summary>
        /// Receive job
        /// </summary>
        /// <returns></returns>
        private byte[] InternalReceive()
        {
            // Don't allow a disconnection, while receive is in progress
            _connectedMutex.WaitOne();

            #region FirstFrame

            _receiveBuffer.Acquire();

            var firstFrameBitArray = _receiveBuffer.Get();

            var firstFrame = Frame.Parse(firstFrameBitArray);

            if (firstFrame.IsStart)
            {
                // Set ReceiverNotReady for some time
                _receiveBuffer.SetResponseStatus(ResponseStatus.RNR);
            }
            else
            {
                _receiveBuffer.SetResponseStatus(ResponseStatus.REJ);
            }

            _receiveBuffer.Release();

            Thread.Sleep(50);
            _receiveBuffer.SetResponseStatus(ResponseStatus.RR);

            #endregion

            List<byte[]> data = new List<byte[]>();

            bool shouldContinue = true;
            while (shouldContinue)
            {
                _receiveBuffer.Acquire();

                List<BitArray> bufferedBitArrays = new List<BitArray>();
                while (_receiveBuffer.HasAvailable())
                {
                    bufferedBitArrays.Add(_receiveBuffer.Get());
                }

                _receiveBuffer.Release();

                BitArray resultingChecksum = new BitArray(C.ChecksumSize);
                var checksumBuilder = new VerticalOddityChecksumBuilder();

                bool success = true;
                for (var i = 0; i < bufferedBitArrays.Count; i++)
                {
                    try
                    {
                        var frame = Frame.Parse(bufferedBitArrays[i]);
                        resultingChecksum.Xor(checksumBuilder.Build(frame.Data));

                        if (frame.IsEnd)
                        {
                            // This is an end frame
                            shouldContinue = false;
                            _receiveBuffer.SetResponseStatus(ResponseStatus.RR);
                            break;
                        }
                        else if (frame.IsControl)
                        {
                            var controlChecksum = frame.Data;
                            if (resultingChecksum.IsSameNoCopy(controlChecksum, 0, 0, C.ChecksumSize))
                            {
                                // Everything is fine
                                break;
                            }
                            else
                            {
                                _receiveBuffer.SetResponseStatus(ResponseStatus.REJ);
                                break;
                            }
                        }

                        Random random = new Random(DateTime.Now.Millisecond);

                        if (random.Next(0, 1000) > 800)
                        {
                            // 1/5 is RNR)

                            _receiveBuffer.SetResponseStatus(ResponseStatus.RNR);
                            Thread.Sleep(100);
                            _receiveBuffer.SetResponseStatus(ResponseStatus.RR);
                            Thread.Sleep(100);
                            break;
                        }
                        else if (random.Next(0, 1000) > 950)
                        {
                            // 5% collision
                            // invert one bit
                            bufferedBitArrays[i][random.Next(0, bufferedBitArrays[i].Length)] ^= true;
                        }

                        var dataChecksum = new VerticalOddityChecksumBuilder().Build(frame.Data);
                        var receivedChecksum = frame.Checksum;
                        if (receivedChecksum.IsSameNoCopy(dataChecksum, 0, 0, C.ChecksumSize))
                        {
                            data.Add(frame.Data.DeBitStaff().ToByteArray());
                            _receiveBuffer.SetResponseStatus(ResponseStatus.RR);
                        }
                        else
                        {
                            _receiveBuffer.SetSrejCount(bufferedBitArrays.Count - i);
                            _receiveBuffer.SetResponseStatus(ResponseStatus.SREJ);
                            break;
                        }
                    }
                    catch (ArgumentException)
                    {
                        _receiveBuffer.SetSrejCount(bufferedBitArrays.Count);
                        _receiveBuffer.SetResponseStatus(ResponseStatus.REJ);
                    }
                }
            }

            _connectedMutex.ReleaseMutex();
            return data.SelectMany(t => t).ToArray();
        }

        /// <summary>
        /// <para>Top level Interface, to send data</para>
        /// <para>Actually enqueues array for later background processing</para>
        /// </summary>
        /// <param name="array"></param>
        public void Send(byte[] array)
        {
            _sendQueue.Enqueue(array);

            // If we added a frame, while send thread was sleepy, invoke it
            if (_sendQueue.Count == 1)
            {
                _sendBarrier.Set();
            }
        }

        /// <summary>
        /// Connects this socket to another
        /// </summary>
        /// <param name="socket"></param>
        public void Connect(ISocket socket)
        {
            if (!_connected)
            {
                // Accept already inversed buffers order
                (_sendBuffer, _receiveBuffer) = socket.AcceptConnect(this);
                _connectedMutex.WaitOne();
                _pairedSocket = socket;
                _connected = true;
                _terminate = false;
                _sendThread = new Thread(SendRoutine);
                _sendThread.Start();
                _receiveThread = new Thread(ReceiveRoutine);
                _receiveThread.Start();
                Connected?.Invoke();
                _connectedMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Disconnects socket from connected one
        /// </summary>
        public void Disconnect()
        {
            if (_connected)
            {
                _connectedMutex.WaitOne();
                _pairedSocket.AcceptDisconnect();
                _connected = false;
                _terminate = true;
                _sendBarrier.Set();
                Disconnected?.Invoke();
                _connectedMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Accepts a connection and returns 2 buffers, first is send, second is receive (inverse order)
        /// </summary>
        /// <returns></returns>
        public (MiddlewareBuffer, MiddlewareBuffer) AcceptConnect(ISocket socket)
        {
            _connectedMutex.WaitOne();
            _receiveBuffer = new MiddlewareBuffer();
            _sendBuffer = new MiddlewareBuffer();
            _connected = true;

            _pairedSocket = socket;

            _terminate = false;

            _sendThread = new Thread(SendRoutine);
            _sendThread.Start();
            _receiveThread = new Thread(ReceiveRoutine);
            _receiveThread.Start();

            Connected?.Invoke();

            _connectedMutex.ReleaseMutex();
            // Inverse buffer order
            return (_receiveBuffer, _sendBuffer);
        }

        /// <summary>
        /// Accepts disconnection request
        /// </summary>
        public void AcceptDisconnect()
        {
            _connectedMutex.WaitOne();
            _connected = false;
            _pairedSocket = null;

            _terminate = true;

            Disconnected?.Invoke();
            _connectedMutex.ReleaseMutex();
        }
    }
}
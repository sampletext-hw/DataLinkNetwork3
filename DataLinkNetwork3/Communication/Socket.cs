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

        public Socket(string title = "Socket Unnamed")
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
        /// <param name="array">Packet Data Array</param>
        private bool PerformSend(byte[] array)
        {
            // BitStaff all data and split to frame size
            var data = new BitArray(array).BitStaff();

            var arrays = data.Split(C.MaxFrameDataSize);

            for (var index = 0; index < arrays.Count; index++)
            {
                var dataBits = arrays[index];
                var addressBits = new BitArray(C.AddressSize);
                var controlBits = new BitArray(C.ControlSize);
               
                // First byte is a frame id, second is control flag
                var controlBytes = new byte[] {(byte)(index & 0xFF), 0};
                controlBits.Writer().Write(new BitArray(controlBytes));

                var frame = new Frame(dataBits, addressBits, controlBits);

                var bitArray = frame.Build();

                var result = InternalSendFrame(bitArray, 0);
                if (!result)
                {
                    return false;
                }
                else
                {
                    Console.WriteLine($"{_title}: Sent frame {index} / {arrays.Count}");
                }
            }

            // Send End Control Frame
            return InternalSendEnd();
        }

        /// <summary>
        /// Small util method, which sends frame with end (0x11) control bits
        /// </summary>
        private bool InternalSendEnd()
        {
            //Console.WriteLine("InternalSendEnd");
            var endAddressBits = new BitArray(C.AddressSize);
            var endControlBits = new BitArray(C.ControlSize);
            var endControlBitsWriter = new BitArrayWriter(endControlBits);
            endControlBitsWriter.Write(new BitArray(new byte[] {0, 0x11}));
            var endFrame = new Frame(new BitArray(0), endAddressBits, endControlBits);

            var endFrameBits = endFrame.Build();

            return InternalSendFrame(endFrameBits, 0);
        }

        /// <summary>
        /// Recursive method, which attempts to send an encoded frame for 3 times
        /// </summary>
        /// <param name="frameBits">Encoded (Built) Frame</param>
        /// <param name="tried">Amount of tries, should be 0 for first invocation</param>
        /// <returns>Result of a send (either true or false)</returns>
        private bool InternalSendFrame(BitArray frameBits, int tried)
        {
            _sendBuffer.Acquire();
            _sendBuffer.Push(frameBits);
            _sendBuffer.Release();

            var lastReceiveStatus = AwaitStatusCode();

            if (lastReceiveStatus == 1)
            {
                // Everything is OK
                _sendBuffer.ResetStatus();
                return true;
            }
            else if (lastReceiveStatus == -1)
            {
                // Receiver returned a failed flag, retry send
                _sendBuffer.ResetStatus();
                if (tried == 3)
                {
                    return false;
                }

                // Recursively send this frame again
                var result = InternalSendFrame(frameBits, tried + 1);
                return result;
            }

            return false;
        }

        /// <summary>
        /// Small utility for awaiting a status code from receiver
        /// </summary>
        /// <returns>Status code from receiver</returns>
        private int AwaitStatusCode()
        {
            var stopwatch = Stopwatch.StartNew();

            var lastReceiveStatus = 0;

            while (lastReceiveStatus == 0)
            {
                lastReceiveStatus = _sendBuffer.GetStatusCode();
                if (lastReceiveStatus == 0)
                {
                    if (stopwatch.ElapsedMilliseconds > C.SendTimeoutMilliseconds)
                    {
                        stopwatch.Stop();
                        lastReceiveStatus = -1;
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
            var framedBytes = new Dictionary<int, byte[]>();

            var lastReceived = -1;

            var receivedEnd = false;

            var idLoop = 0;

            var totalReceived = 0;

            // It's actually changing inside, dumb Resharper!
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            while (!receivedEnd)
            {
                // If for some reason, there is no data, but we didn't receive an end frame, wait for data
                while (!_receiveBuffer.HasAvailable())
                {
                    Thread.Sleep(10);
                }
                
                // Receive and parse a frame
                var bitArray = InternalReceiveFrame();
                var frame = Frame.Parse(bitArray);
                
                // Read and process control bits
                var controlBytes = frame.Control.Reader().Read(16).ToByteArray();
                var frameId = controlBytes[0];
                receivedEnd = controlBytes[1] == 0x11;

                // If this is a End Frame - break
                if (receivedEnd)
                {
                    _receiveBuffer.SetStatusCode(1);
                    break;
                }
                
                // (loop id over 255)
                if (lastReceived == byte.MaxValue)
                {
                    lastReceived = 0;
                    idLoop++;
                }

                if (frameId <= lastReceived)
                {
                    totalReceived++;
                    Console.WriteLine($"{_title}: Received {totalReceived} frame");
                    _receiveBuffer.SetStatusCode(1);
                }
                else
                {
                    lastReceived = frameId;

                    // Check for checksum integrity
                    
                    var checksum = new VerticalOddityChecksumBuilder().Build(frame.Data);
                    if (frame.Checksum.IsSameNoCopy(checksum, 0, 0, C.ChecksumSize))
                    {
                        _receiveBuffer.SetStatusCode(1);
                        framedBytes.Add(idLoop * byte.MaxValue + frameId, frame.Data.DeBitStaff().ToByteArray());
                    }
                    else
                    {
                        _receiveBuffer.SetStatusCode(-1);
                    }
                }
            }

            var total = framedBytes.OrderBy(f => f.Key).SelectMany(b => b.Value).ToList();

            // Release connection locker, so a disconnect can be performed
            _connectedMutex.ReleaseMutex();
            return total.ToArray();
        }

        /// <summary>
        /// Small utility, to receive a single frame from _receiveBuffer
        /// </summary>
        /// <returns>Frame Bits</returns>
        private BitArray InternalReceiveFrame()
        {
            _receiveBuffer.Acquire();
            var bitArray = _receiveBuffer.Get();
            _receiveBuffer.Release();

            return bitArray;
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
            _sendBarrier.Set();

            Disconnected?.Invoke();
            _connectedMutex.ReleaseMutex();
        }
    }
}
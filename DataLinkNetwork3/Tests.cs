using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using DataLinkNetwork3.Abstractions;
using DataLinkNetwork3.BitArrayRoutine;
using DataLinkNetwork3.Checksum;
using DataLinkNetwork3.Communication;

namespace DataLinkNetwork3
{
    public class Tests
    {
        public static void Test1()
        {
            BitArray testData = new BitArray(64);

            IChecksumBuilder builder = new VerticalOddityChecksumBuilder();

            var checksum = builder.Build(testData);

            Console.WriteLine(checksum.ToBinString());
        }

        public static void Test2()
        {
            BitArray testData = new BitArray(64);
            Random random = new Random(DateTime.Now.Millisecond);

            for (var i = 0; i < testData.Count; i++)
            {
                testData[i] = random.Next(0, 100) >= 50;
            }

            Frame frame = new Frame(testData, new BitArray(C.AddressSize), new BitArray(C.ControlSize));
            var rawFrameBits = frame.Build();

            Console.WriteLine(frame.ToString());
            Console.WriteLine(rawFrameBits.ToBinString());
        }

        public static void Test3()
        {
            BitArray testData = new BitArray(5);
            Random random = new Random(DateTime.Now.Millisecond);

            for (var i = 0; i < testData.Count; i++)
            {
                testData[i] = random.Next(0, 100) >= 50;
            }

            Frame frame = new Frame(testData, new BitArray(C.AddressSize), new BitArray(C.ControlSize));
            var rawFrameBits = frame.Build();

            var parsedHdlcFrame = Frame.Parse(rawFrameBits);

            Console.WriteLine(frame.ToString());
            Console.WriteLine(parsedHdlcFrame.ToString());
        }

        // public static void Test4()
        // {
        //     ISender sender = new Sender();
        //     IReceiver receiver = new Receiver();
        //
        //     sender.Connect(receiver);
        //
        //     Task.Run(() =>
        //     {
        //         sender.Send(
        //             Encoding.UTF8.GetBytes(
        //                 "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
        //                 "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. " +
        //                 "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. " +
        //                 "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum."
        //             )
        //         );
        //         sender.Disconnect(receiver);
        //     });
        //     Task.Run(() =>
        //     {
        //         var receivedBytes = receiver.Receive();
        //
        //         var receive = Encoding.UTF8.GetString(receivedBytes);
        //
        //         Console.WriteLine(receive);
        //     });
        //
        //     Console.ReadKey();
        // }

        public static void Test5()
        {
            ISocket socket1 = new Socket();
            ISocket socket2 = new Socket();

            socket1.Connected += () => { Console.WriteLine("Socket1: Connected"); };
            socket2.Connected += () => { Console.WriteLine("Socket2: Connected"); };

            socket1.Disconnected += () => { Console.WriteLine("Socket1: Disconnected"); };
            socket2.Disconnected += () => { Console.WriteLine("Socket2: Disconnected"); };

            socket1.Received += bytes => { Console.WriteLine($"Socket1: Received ({Encoding.UTF8.GetString(bytes)})"); };
            socket2.Received += bytes => { Console.WriteLine($"Socket2: Received ({Encoding.UTF8.GetString(bytes)})"); };

            socket1.StartedSending += () => { Console.WriteLine("Socket1: Started Sending"); };
            socket2.StartedSending += () => { Console.WriteLine("Socket2: Started Sending"); };

            socket1.StartedReceiving += () => { Console.WriteLine("Socket1: Started Receiving"); };
            socket2.StartedReceiving += () => { Console.WriteLine("Socket2: Started Receiving"); };

            socket1.Connect(socket2);

            // socket1.Send(Encoding.UTF8.GetBytes($"Test message"));

            for (int i = 0; i < 30; i++)
            {
                socket1.Send(Encoding.UTF8.GetBytes($"Test message {i}"));
            }

            for (int i = 0; i < 30; i++)
            {
                socket2.Send(Encoding.UTF8.GetBytes($"Test message {i}"));
            }

            Thread.Sleep(10000);
            socket1.Disconnect();
        }

        public static void Test6()
        {
            ISocket socket1 = new Socket("Socket1");
            ISocket socket2 = new Socket("Socket2");

            socket1.Connected += () => { Console.WriteLine("Socket1: Connected"); };
            socket2.Connected += () => { Console.WriteLine("Socket2: Connected"); };

            socket1.Disconnected += () => { Console.WriteLine("Socket1: Disconnected"); };
            socket2.Disconnected += () => { Console.WriteLine("Socket2: Disconnected"); };

            socket1.Received += bytes =>
            {
                Console.WriteLine($"Socket1: Received ({bytes.Length}) bytes");
                Console.WriteLine($"Socket1: Received ({Encoding.UTF8.GetString(bytes)})");
            };
            socket2.Received += bytes =>
            {
                Console.WriteLine($"Socket2: Received ({bytes.Length}) bytes");
                File.WriteAllBytes("file.txt", bytes);
                Process.Start("notepad.exe", "file.txt");
            };

            socket1.StartedSending += () => { Console.WriteLine("Socket1: Started Sending"); };
            socket2.StartedSending += () => { Console.WriteLine("Socket2: Started Sending"); };

            socket1.StartedReceiving += () => { Console.WriteLine("Socket1: Started Receiving"); };
            socket2.StartedReceiving += () => { Console.WriteLine("Socket2: Started Receiving"); };

            socket1.Connect(socket2);

            // Simulate sending file from first to second, and 2 texts from second to first, so we can ensure, duplex is working
            
            var fileBytes = File.ReadAllBytes(@"C:\Users\Admin\Downloads\egop awesome file.txt");

            socket1.Send(fileBytes);

            socket2.Send(
                Encoding.UTF8.GetBytes(
                    "Lorem ipsum dolor sit amet, consectetur adipiscing elit, " +
                    "sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                    "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. " +
                    "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. " +
                    "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum."
                )
            );

            socket2.Send(
                Encoding.UTF8.GetBytes(
                    "Lorem ipsum dolor sit amet, consectetur adipiscing elit, " +
                    "sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                    "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. " +
                    "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. " +
                    "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum."
                )
            );

            Console.ReadKey();

            socket1.Disconnect();
        }
    }
}
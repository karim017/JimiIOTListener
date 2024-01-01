/*using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}


*/


// A C# Program for Server
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ConcoxServer;
namespace Server
{

    class Program
    {
        // const string IP = "127.0.0.1";

        // const string IP = "172.16.1.3";

        // const string IP = "115.69.211.195";


        // Main Method
        static void Main(string[] args)
        {
            //  ExecuteServer();



            string IP = AppConfigSection.IP; ;
            int PORT = AppConfigSection.Port; ;
            Boolean test = true;


            // LogWriter.WriteLog(IP);
            GPS gps = new GPS(IP, PORT, test);

            Console.WriteLine("Connection Ended");

            LogWriter.WriteLog("Connection Ended");
        }

        public static void ExecuteServer()
        {
            // Establish the local endpoint 
            // for the socket. Dns.GetHostName
            // returns the name of the host 
            // running the application.
            IPHostEntry ipHost = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress ipAddr = ipHost.AddressList[0];
            IPEndPoint localEndPoint = new IPEndPoint(ipAddr, 3389);

            // Creation TCP/IP Socket using 
            // Socket Class Constructor
            Socket listener = new Socket(ipAddr.AddressFamily,
                        SocketType.Stream, ProtocolType.Tcp);

            try
            {

                // Using Bind() method we associate a
                // network address to the Server Socket
                // All client that will connect to this 
                // Server Socket must know this network
                // Address
                listener.Bind(localEndPoint);

                // Using Listen() method we create 
                // the Client list that will want
                // to connect to Server
                listener.Listen(10);

                while (true)
                {

                    Console.WriteLine("Waiting connection ... ");
                    LogWriter.WriteLog("Waiting connection ...");
                    // Suspend while waiting for
                    // incoming connection Using 
                    // Accept() method the server 
                    // will accept connection of client
                    Socket clientSocket = listener.Accept();

                    // Data buffer
                    byte[] bytes = new Byte[1024];
                    string data = null;

                    while (true)
                    {

                        int numByte = clientSocket.Receive(bytes);

                        data += Encoding.ASCII.GetString(bytes,
                                                0, numByte);

                        if (data.IndexOf("<EOF>") > -1)
                            break;
                    }

                    Console.WriteLine("Text received -> {0} ", data);
                    LogWriter.WriteLog("\nText received ->  " + data);
                    byte[] message = Encoding.ASCII.GetBytes("Test Server");

                    // Send a message to Client 
                    // using Send() method
                    clientSocket.Send(message);

                    // Close client Socket using the
                    // Close() method. After closing,
                    // we can use the closed Socket 
                    // for a new Client Connection
                    clientSocket.Shutdown(SocketShutdown.Both);
                    clientSocket.Close();
                }
            }

            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}


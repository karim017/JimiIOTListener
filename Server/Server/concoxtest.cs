﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Data;
using System.Data.SqlClient;
using System.ComponentModel;
using System.Timers;
using System.Configuration;
using Server;
namespace ConcoxServer
{
    public class StateObject
    {
        public long connectionNumber = -1;
        public Socket workSocket { get; set; }
        public byte[] buffer { get; set; }
        public int fifoCount { get; set; }
        public string IMEI { get; set; }
    }

    public enum PROTOCOL_NUMBER
    {
        LOGIN_MESSAGE = 0x01,
        LOCATION_DATA = 0x12,
        STATUS_INFO = 0X13,
        STRING_INFO = 0X15,
        ALARM_DATA = 0X16,
        GPS_QUERY_ADDR_PHONE_NUM = 0X1A,
        COMMAND_INFO = 0X80,
        NONE
    }
    public enum UNPACK_STATUS
    {
        ERROR,
        NOT_ENOUGH_BYTES,
        BAD_CRC,
        GOOD_MESSAGE,
        DEFAULT
    }
    public enum PROCESS_STATE
    {
        ACCEPT,
        READ,
        PROCESS,
        UNPACK
    }
    public enum ALARM : Byte
    {
        NORMAL = 0,
        SHOCK_ALARM = 1,
        POWER_CUT_ALARM = 2,
        LOW_BATTERY = 3,
        SOS = 4
    }



    /*


        // Commented Code
    class Program
    {
        const string IP = "127.0.0.1";
        const int PORT = 8841;
        const Boolean test = true;

        static void Main(string[] args)
        {

            GPS gps = new GPS(IP, PORT, test);

            Console.WriteLine("Connection Ended");

        }
    }

    */



    public class GPS
    {
        const int BUFFER_SIZE = 1024;
        //const string CONNECT_STRING = @"Data Source=.\SQLEXPRESS;Initial Catalog=GPSTracker;Integrated Security=SSPI;";

        //  const string CONNECT_STRING = @"Data Source=172.16.8.32\MSSQL2014;Initial Catalog=GPSTracker;User ID=Admin;Password=AdMsSql2014";
        // const string CONNECT_STRING = @"Data Source=115.69.208.44\mssql2014;Initial Catalog=GPSTracker;User ID=sa;Password=SaSql2014";
         string CONNECT_STRING = System.Configuration.ConfigurationManager.AppSettings["ConnStr"].ToString();


        const string LOGIN_INSERT_COMMMAND_TEXT = "use GPSTracker INSERT INTO Login (TerminalID,Date) VALUES(@TerminalID,@Date)";
        const string LOCATION_INSERT_COMMMAND_TEXT = "INSERT INTO T_Tracking (IMEI,TrackTime, CurrTime, Longitude, Lattitude,speed)" +
            "VALUES (@IMEI, @TrackTime, @CurrTime, @Longitude, @Lattitude, @speed)";
        static SqlConnection conn = null;
        static SqlCommand cmdLogin = null;
        static SqlCommand cmdLocation = null;

        static byte[] loginResponse = { 0x78, 0x78, 0x05, 0x01, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x0A };
        static byte[] alarmResponse = { 0x78, 0x78, 0x05, 0x16, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x0A };
        static byte[] alarmDataAddressResponse = { 0x78, 0x78, 0x00, 0x97, 0x7E,
                                          0x00, 0x00, 0x00, 0x01,
                                          0x41, 0x4C, 0x41, 0x52, 0x4D, 0x53, 0x4D, 0x53,   //ALARMS
                                          0x26, 0x26,                                       //&&
                                          0x80, 0x00, 0x72, 0x00, 0x79, 0x00, 0x78, 0x00,   // PHONE HOME
                                          0x69, 0x00, 0x32, 0x00, 0x72, 0x00, 0x79, 0x00,
                                          0x77, 0x00, 0x69,
                                          0x26, 0x26,                                       //&&
                                          0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,   // Phone Numbe
                                          0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                                          0x00, 0x00, 0x00, 0x00, 0x00,
                                          0x23, 0x23,                                       //##
                                          0x00, 0x00,                                       //serial number
                                          0x00, 0x00,                                       //check bytes
                                          0x0D, 0x0A                                        //stop bytes
                                      };
        static byte[] heartbeatResponse = { 0x78, 0x78, 0x05, 0x13, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x0A };
        static byte[] connectOilAndEletricity = {
                                          0x78, 0x78, 0x16, 0x80, 0x10, 0x12, 0x34, 0x56, 0x78, 0x48,  //server flag bit 0x12345678
                                          0x46, 0x59, 0x44, 0x2C, 0x30, 0x30, 0x30, 0x30, 0x30, 0x30,
                                          0x23, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x0A
                                     };

        static long connectionNumber = 0;
        //mapping of connection number to StateObject
        static Dictionary<long, KeyValuePair<List<byte>, StateObject>> connectionDict = new Dictionary<long, KeyValuePair<List<byte>, StateObject>>();
        //fifo contains list of connections number wait with receive data
        public static List<long> fifo = new List<long>();

        public static AutoResetEvent allDone = new AutoResetEvent(false);
        public static AutoResetEvent acceptDone = new AutoResetEvent(false);

        public GPS(string IP, int port, Boolean test)
        {
            try
            {
                conn = new SqlConnection(CONNECT_STRING);
                conn.Open();

                cmdLogin = new SqlCommand(LOGIN_INSERT_COMMMAND_TEXT, conn);
                cmdLogin.Parameters.Add("@TerminalID", SqlDbType.NVarChar, 8);
                cmdLogin.Parameters.Add("@Date", SqlDbType.DateTime);

                cmdLocation = new SqlCommand(LOCATION_INSERT_COMMMAND_TEXT, conn);
                cmdLocation.Parameters.Add("@IMEI", SqlDbType.NVarChar, 50);
                cmdLocation.Parameters.Add("@TrackTime", SqlDbType.DateTime);
                cmdLocation.Parameters.Add("@currTime", SqlDbType.DateTime);
                cmdLocation.Parameters.Add("@Longitude", SqlDbType.NChar, 50);
                cmdLocation.Parameters.Add("@Lattitude", SqlDbType.NVarChar, 50);
                cmdLocation.Parameters.Add("@speed", SqlDbType.Float);

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error : '{0}'", ex.Message);
                LogWriter.WriteLog("\nError  " + ex.Message);

                //Console.ReadLine();
                return;
            }

            try
            {
                //initialize the timer for writing to database.
                WriteDBAsync.WriteDatabase();
                StartListening(IP, port, test);

                // Open 2nd listener to simulate two devices, Only for testing
                //StartListening(IP, port + 1, test);

                ProcessMessages();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error : '{0}'", ex.Message);
                //Console.ReadLine();
                return;
            }
        }
        public enum DATABASE_MESSAGE_TYPE
        {
            LOGIN,
            LOCATION
        }
        public class WriteDBMessage
        {
            public DATABASE_MESSAGE_TYPE message { get; set; }
        }
        public class WriteDBMessageLogin : WriteDBMessage
        {
            public DateTime date { get; set; }
            public string IMEI { get; set; }
        }
        public class WriteDBMessageLocation : WriteDBMessage
        {
            public string IMEI { get; set; }
            public DateTime trackTime { get; set; }
            public DateTime currTime { get; set; }
            public byte[] longitude { get; set; }
            public byte[] lattitude { get; set; }
            public float speed { get; set; }
            public byte[] courseStatus { get; set; }
        }

        public static class WriteDBAsync
        {
            public enum Mode
            {
                READ,
                WRITE
            }
            public static List<WriteDBMessage> fifo = new List<WriteDBMessage>();
            public static System.Timers.Timer timer = null;

            public static void WriteDatabase()
            {
                timer = new System.Timers.Timer(1000);
                timer.Elapsed += Timer_Elapsed;
                timer.Start();
            }
            public static WriteDBMessage ReadWriteFifo(Mode mode, WriteDBMessage message)
            {
                Object thisLock2 = new Object();

                lock (thisLock2)
                {
                    switch (mode)
                    {
                        case Mode.READ:
                            if (fifo.Count > 0)
                            {
                                message = fifo[0];
                                fifo.RemoveAt(0);
                            }
                            break;
                        case Mode.WRITE:
                            fifo.Add(message);
                            break;
                    }

                }
                return message;
            }
            static void Timer_Elapsed(object sender, ElapsedEventArgs e)
            {
                timer.Enabled = false;
                WriteDBMessage row = null;
                int rowsAdded = 0;
                uint number = 0;
                double lat = 0;
                string latStr = "";
                double lon = 0;
                string longStr = "";
                int degrees = 0;
                double minutes = 0;
                try
                {

                    while ((row = ReadWriteFifo(Mode.READ, null)) != null)
                    {
                        switch (row.message)
                        {
                            case DATABASE_MESSAGE_TYPE.LOGIN:
                                cmdLogin.Parameters["@TerminalID"].Value = ((WriteDBMessageLogin)row).IMEI;
                                cmdLogin.Parameters["@Date"].Value = ((WriteDBMessageLogin)row).date;
                                rowsAdded = cmdLogin.ExecuteNonQuery();
                                break;
                            case DATABASE_MESSAGE_TYPE.LOCATION:
                                cmdLocation.Parameters["@IMEI"].Value = ((WriteDBMessageLocation)row).IMEI;
                                cmdLocation.Parameters["@TrackTime"].Value = ((WriteDBMessageLocation)row).trackTime;
                                cmdLocation.Parameters["@currTime"].Value = ((WriteDBMessageLocation)row).currTime;

                                number = BitConverter.ToUInt32(((WriteDBMessageLocation)row).longitude.Reverse().ToArray(), 0);
                                lon = (180 * number) / 324000000.0;

                                degrees = (int)lon;
                                minutes = 60 * (lon - degrees);
                                longStr = string.Format("{0}º{1}{2}", degrees, minutes, (((WriteDBMessageLocation)row).courseStatus[0] & 0x08) == 0 ? "E" : "W");

                                cmdLocation.Parameters["@Longitude"].Value = longStr;

                                number = BitConverter.ToUInt32(((WriteDBMessageLocation)row).lattitude.Reverse().ToArray(), 0);
                                lat = (90 * number) / 162000000.0;

                                degrees = (int)lat;
                                minutes = 60 * (lat - degrees);
                                latStr = string.Format("{0}º{1}{2}", degrees, minutes, (((WriteDBMessageLocation)row).courseStatus[0] & 0x04) == 0 ? "S" : "N");

                                cmdLocation.Parameters["@Lattitude"].Value = latStr;
                                cmdLocation.Parameters["@speed"].Value = ((WriteDBMessageLocation)row).speed;
                                rowsAdded = cmdLocation.ExecuteNonQuery();
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Console.WriteLine("Error : '{0}'", ex.Message);
                }
                timer.Enabled = true;
            }
        }

        public void StartListening(string IP, int port, Boolean test)
        {
            try
            {
                // Establish the local endpoint for the socket.
                // The DNS name of the computer
                // running the listener is "host.contoso.com".
                IPHostEntry ipHostInfo = Dns.GetHostEntry(IP);  //Dns.Resolve(Dns.GetHostName());
                IPAddress ipAddress = IPAddress.Parse(IP);  //ipHostInfo.AddressList[0];
                //IPAddress local = IPAddress.Parse(IP);

                IPEndPoint localEndPoint = null;
                if (test)
                {
                    localEndPoint = new IPEndPoint(IPAddress.Any, port);
                }
                else
                {
                    localEndPoint = new IPEndPoint(ipAddress, port);
                }

                // Create a TCP/IP socket.
                Socket listener = new Socket(AddressFamily.InterNetwork,
                    SocketType.Stream, ProtocolType.Tcp);
                // Bind the socket to the local endpoint and listen for incoming connections.


                allDone.Reset();
                acceptDone.Reset();
                listener.Bind(localEndPoint);
                listener.Listen(100);

                //login code, wait for 1st message
                Console.WriteLine("Wait 5 seconds for login message");

                listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }


        public void ProcessMessages()
        {
            UInt16 sendCRC = 0;
            DateTime date;
            int year = 0;
            int month = 0;
            int day = 0;
            int hour = 0;
            int minute = 0;
            int second = 0;

            KeyValuePair<List<byte>, StateObject> byteState;
            KeyValuePair<UNPACK_STATUS, byte[]> status;
            byte[] receiveMessage = null;
            StateObject state = null;
            byte[] serialNumber = null;
            byte[] serverFlagBit = null;
            byte[] stringArray = null;
            string stringMessage = "";
            byte lengthOfCommand = 0;
            PROTOCOL_NUMBER protocolNumber = PROTOCOL_NUMBER.NONE;

            try
            {
                Boolean firstMessage = true;
                acceptDone.Set();
                //loop forever
                while (true)
                {
                    allDone.WaitOne();

                    //read fifo until empty
                    while (true)
                    {
                        //read one connection until buffer doesn't contain any more packets
                        byteState = ReadWrite(PROCESS_STATE.PROCESS, null, null, -1);

                        if (byteState.Value.fifoCount == -1) break;

                        state = byteState.Value;
                        while (true)
                        {
                            status = Unpack(byteState);
                            if (status.Key == UNPACK_STATUS.NOT_ENOUGH_BYTES)
                                break;

                            if (status.Key == UNPACK_STATUS.ERROR)
                            {
                                Console.WriteLine("Error : Bad Receive Message, Data");
                                break;
                            }

                            //message is 2 start bytes + 1 byte (message length) + 1 byte message length + 2 end bytes
                            receiveMessage = status.Value;

                            int messageLength = receiveMessage[2];
                            Console.WriteLine("Status : '{0}', Receive Message : '{1}'", status.Key == UNPACK_STATUS.GOOD_MESSAGE ? "Good" : "Bad", BytesToString(receiveMessage.Take(messageLength + 5).ToArray()));


                            LogWriter.WriteLog("\nMSG :   " + BytesToString(receiveMessage.Take(messageLength + 5).ToArray()));

                            if (status.Key != UNPACK_STATUS.GOOD_MESSAGE)
                            {
                                break;
                            }
                            else
                            {
                                if (firstMessage)
                                {
                                    if (receiveMessage[3] != 0x01)
                                    {
                                        Console.WriteLine("Error : Expected Login Message : '{0}'", BytesToString(receiveMessage));
                                        break;
                                    }
                                    firstMessage = false;
                                }

                                //skip start bytes, message length.  then go back 4 bytes (CRC and serial number)
                                serialNumber = receiveMessage.Skip(2 + 1 + messageLength - 4).Take(2).ToArray();

                                protocolNumber = (PROTOCOL_NUMBER)receiveMessage[3];
                                Console.WriteLine("Protocol Number : '{0}'", protocolNumber.ToString());
                                switch (protocolNumber)
                                {
                                    case PROTOCOL_NUMBER.LOGIN_MESSAGE:
                                        serialNumber.CopyTo(loginResponse, 4);

                                        sendCRC = crc_bytes(loginResponse.Skip(2).Take(loginResponse.Length - 6).ToArray());

                                        loginResponse[loginResponse.Length - 4] = (byte)((sendCRC >> 8) & 0xFF);
                                        loginResponse[loginResponse.Length - 3] = (byte)((sendCRC) & 0xFF);

                                        //
                                        string IMEI = Encoding.ASCII.GetString(receiveMessage.Skip(4).Take(messageLength - 5).ToArray());
                                        byteState.Value.IMEI = IMEI;

                                        Console.WriteLine("Received good login message from Serial Number : '{0}', Terminal ID = '{1}'", "0x" + serialNumber[0].ToString("X2") + serialNumber[1].ToString("X2"), IMEI);

                                        Console.WriteLine("Send Message : '{0}'", BytesToString(loginResponse));
                                        Send(state.workSocket, loginResponse);
                                      
                                        WriteDBMessageLogin loginMessage = new WriteDBMessageLogin() { message = DATABASE_MESSAGE_TYPE.LOGIN, IMEI = IMEI, date = DateTime.Now };

                                        WriteDBAsync.ReadWriteFifo(WriteDBAsync.Mode.WRITE, loginMessage);

                                        Console.WriteLine("Wrote to database");
                                        break;
                                    case PROTOCOL_NUMBER.LOCATION_DATA:
                                        year = receiveMessage[4];
                                        month = receiveMessage[5];
                                        day = receiveMessage[6];
                                        hour = receiveMessage[7];
                                        minute = receiveMessage[8];
                                        second = receiveMessage[9];

                                        date = new DateTime(2000 + year, month, day, hour, minute, second);

                                        WriteDBMessageLocation locationMessage = new WriteDBMessageLocation();
                                        locationMessage.message = DATABASE_MESSAGE_TYPE.LOCATION;

                                        locationMessage.trackTime = date;
                                        locationMessage.currTime = DateTime.Now;

                                        locationMessage.lattitude = new byte[4];
                                        Array.Copy(receiveMessage, 11, locationMessage.lattitude, 0, 4);

                                        locationMessage.longitude = new byte[4];
                                        Array.Copy(receiveMessage, 15, locationMessage.longitude, 0, 4);
                                        locationMessage.speed = receiveMessage[19];

                                        locationMessage.courseStatus = new byte[2];
                                        Array.Copy(receiveMessage, 20, locationMessage.courseStatus, 0, 2);

                                        locationMessage.IMEI = byteState.Value.IMEI;
                                        WriteDBAsync.ReadWriteFifo(WriteDBAsync.Mode.WRITE, locationMessage);


                                        Console.WriteLine("Received good location message from Serial Number '{0}', Time = '{1}'", "0x" + serialNumber[0].ToString("X2") + serialNumber[1].ToString("X2"), date.ToLongDateString());
                                        break;

                                    case PROTOCOL_NUMBER.ALARM_DATA:

                                        //first response
                                        int alarmPacketLen = alarmResponse.Length - 5;
                                        alarmResponse[2] = (byte)(alarmPacketLen & 0xFF);

                                        serialNumber.CopyTo(alarmResponse, alarmPacketLen - 1);

                                        sendCRC = crc_bytes(alarmResponse.Skip(2).Take(alarmPacketLen - 1).ToArray());

                                        alarmResponse[alarmPacketLen + 1] = (byte)((sendCRC >> 8) & 0xFF);
                                        alarmResponse[alarmPacketLen + 2] = (byte)((sendCRC) & 0xFF);

                                        Console.WriteLine("Send Alarm Response Message : '{0}'", BytesToString(alarmResponse));
                                        Send(state.workSocket, alarmResponse);


                                        //second response
                                        year = receiveMessage[4];
                                        month = receiveMessage[5];
                                        day = receiveMessage[6];
                                        hour = receiveMessage[7];
                                        minute = receiveMessage[8];
                                        second = receiveMessage[9];

                                        date = new DateTime(2000 + year, month, day, hour, minute, second);
                                        Console.WriteLine("Received good alarm message from Serial Number '{0}', Time = '{1}'", "0x" + serialNumber[0].ToString("X2") + serialNumber[1].ToString("X2"), date.ToLongDateString());
                                        int alarmDataAddressPacketLen = alarmDataAddressResponse.Length - 5;
                                        alarmDataAddressResponse[2] = (byte)(alarmDataAddressPacketLen & 0xFF);

                                        serialNumber.CopyTo(alarmDataAddressResponse, alarmDataAddressPacketLen - 1);

                                        sendCRC = crc_bytes(alarmDataAddressResponse.Skip(2).Take(alarmDataAddressPacketLen - 1).ToArray());

                                        alarmDataAddressResponse[alarmDataAddressPacketLen + 1] = (byte)((sendCRC >> 8) & 0xFF);
                                        alarmDataAddressResponse[alarmDataAddressPacketLen + 2] = (byte)((sendCRC) & 0xFF);

                                        Console.WriteLine("Send Alarm Data Address Message : '{0}'", BytesToString(alarmDataAddressResponse));
                                        Send(state.workSocket, alarmDataAddressResponse);

                                        break;

                                    case PROTOCOL_NUMBER.STATUS_INFO:
                                        serialNumber.CopyTo(heartbeatResponse, 4);

                                        byte info = receiveMessage[4];
                                        byte voltage = receiveMessage[5];
                                        byte GSMsignalStrength = receiveMessage[6];
                                        UInt16 alarmLanguage = (UInt16)((receiveMessage[7] << 8) | receiveMessage[8]);

                                        ALARM alarm = (ALARM)((info >> 3) & 0x07);

                                        sendCRC = crc_bytes(heartbeatResponse.Skip(2).Take(heartbeatResponse.Length - 6).ToArray());

                                        heartbeatResponse[heartbeatResponse.Length - 4] = (byte)((sendCRC >> 8) & 0xFF);
                                        heartbeatResponse[heartbeatResponse.Length - 3] = (byte)((sendCRC) & 0xFF);

                                        Console.WriteLine("Received good status message from Serial Number : '{0}', INFO : '0x{1}{2}{3}{4}'",
                                            "0x" + serialNumber[0].ToString("X2") + serialNumber[1].ToString("X2"),
                                            info.ToString("X2"), voltage.ToString("X2"), GSMsignalStrength.ToString("X2"),
                                            alarmLanguage.ToString("X4"));

                                        Console.WriteLine("Send Message : '{0}'", BytesToString(heartbeatResponse));
                                        Send(state.workSocket, heartbeatResponse);

                                        switch (alarm)
                                        {
                                            //reset cut off alarm
                                            case ALARM.POWER_CUT_ALARM:
                                                int connectOilAndElectricityPacketLen = connectOilAndEletricity.Length - 5;
                                                serialNumber.CopyTo(connectOilAndEletricity, connectOilAndElectricityPacketLen - 1);
                                                sendCRC = crc_bytes(connectOilAndEletricity.Skip(2).Take(connectOilAndEletricity.Length - 6).ToArray());
                                                connectOilAndEletricity[connectOilAndEletricity.Length - 4] = (byte)((sendCRC >> 8) & 0xFF);
                                                connectOilAndEletricity[connectOilAndEletricity.Length - 3] = (byte)((sendCRC) & 0xFF);

                                                serverFlagBit = new byte[4];
                                                Array.Copy(connectOilAndEletricity, 5, serverFlagBit, 0, 4);

                                                lengthOfCommand = connectOilAndEletricity[4];
                                                stringArray = new byte[lengthOfCommand - 4]; //do not include server flag bit
                                                Array.Copy(connectOilAndEletricity, 9, stringArray, 0, lengthOfCommand - 4);
                                                stringMessage = Encoding.ASCII.GetString(stringArray);

                                                Console.WriteLine("Reset Oil and Electricity, Server Flag Bit : '{0}{1}{2}{3}', Message : '{4}'",
                                                  serverFlagBit[0].ToString("X2"),
                                                  serverFlagBit[1].ToString("X2"),
                                                  serverFlagBit[2].ToString("X2"),
                                                  serverFlagBit[3].ToString("X2"),
                                                  stringMessage);
                                                Send(state.workSocket, connectOilAndEletricity);
                                                break;
                                        }

                                        break;

                                    case PROTOCOL_NUMBER.STRING_INFO:
                                        lengthOfCommand = receiveMessage[4];
                                        serverFlagBit = new byte[4];
                                        Array.Copy(receiveMessage, 5, serverFlagBit, 0, 4);
                                        stringArray = new byte[lengthOfCommand - 4]; //do not include server flag bit
                                        Array.Copy(receiveMessage, 9, stringArray, 0, lengthOfCommand - 4);
                                        stringMessage = Encoding.ASCII.GetString(stringArray);

                                        Console.WriteLine("String Message, Server Flag Bit : '{0}{1}{2}{3}', Message : '{4}'",
                                            serverFlagBit[0].ToString("X2"),
                                            serverFlagBit[1].ToString("X2"),
                                            serverFlagBit[2].ToString("X2"),
                                            serverFlagBit[3].ToString("X2"),
                                            stringMessage);

                                        break;

                                } //end switch
                            }// End if
                        } //end while
                    }//end while fifo > 0
                    allDone.Reset();
                }//end while true
            }
            catch (Exception e)
            {

                Console.WriteLine(e.Message);
            }

        }

        static string BytesToString(byte[] bytes)
        {

            return string.Join("", bytes.Select(x => x.ToString("X2")));
        }
        static KeyValuePair<UNPACK_STATUS, byte[]> Unpack(KeyValuePair<List<byte>, StateObject> bitState)
        {
            List<byte> working_buffer = bitState.Key;
            //return null indicates an error
            if (working_buffer.Count() < 3) return new KeyValuePair<UNPACK_STATUS, byte[]>(UNPACK_STATUS.NOT_ENOUGH_BYTES, null);

            int len = working_buffer[2];

            if (working_buffer.Count < len + 5) return new KeyValuePair<UNPACK_STATUS, byte[]>(UNPACK_STATUS.NOT_ENOUGH_BYTES, null);
            // check start and end bytes
            // remove message fro workig buffer and dictionary 
            KeyValuePair<List<byte>, StateObject> byteState = ReadWrite(PROCESS_STATE.UNPACK, null, null, bitState.Value.connectionNumber);
            if (byteState.Key.Count == 0) return new KeyValuePair<UNPACK_STATUS, byte[]>(UNPACK_STATUS.ERROR, null);

            List<byte> packet = byteState.Key;

            //crc test
            byte[] crc = packet.Skip(len + 1).Take(2).ToArray();
            ushort crcShort = (ushort)((crc[0] << 8) | crc[1]);
            //skip start bytes, crc, and end bytes
            ushort CalculatedCRC = crc_bytes(packet.Skip(2).Take(len - 1).ToArray());

            if (CalculatedCRC != crcShort)
            {
                return new KeyValuePair<UNPACK_STATUS, byte[]>(UNPACK_STATUS.BAD_CRC, packet.ToArray());
            }

            return new KeyValuePair<UNPACK_STATUS, byte[]>(UNPACK_STATUS.GOOD_MESSAGE, packet.ToArray());
        }
        static public UInt16 crc_bytes(byte[] data)
        {
            ushort crc = 0xFFFF;

            for (int i = 0; i < data.Length; i++)
            {
                crc ^= (ushort)(Reflect(data[i], 8) << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) > 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }
            crc = Reflect(crc, 16);
            crc = (ushort)~crc;
            return crc;
        }
        static public ushort Reflect(ushort data, int size)
        {
            ushort output = 0;
            for (int i = 0; i < size; i++)
            {
                int lsb = data & 0x01;
                output = (ushort)((output << 1) | lsb);
                data >>= 1;
            }
            return output;
        }

        static KeyValuePair<List<byte>, StateObject> ReadWrite(PROCESS_STATE ps, Socket handler, IAsyncResult ar, long unpackConnectionNumber)
        {
            KeyValuePair<List<byte>, StateObject> byteState = new KeyValuePair<List<byte>, StateObject>(); ;
            StateObject stateObject = null;
            int bytesRead = -1;
            int workingBufferLen = 0;
            List<byte> working_buffer = null;
            byte[] buffer = null;

            Object thisLock1 = new Object();

            lock (thisLock1)
            {
                switch (ps)
                {
                    case PROCESS_STATE.ACCEPT:

                        acceptDone.WaitOne();
                        acceptDone.Reset();
                        stateObject = new StateObject();
                        stateObject.buffer = new byte[BUFFER_SIZE];
                        connectionDict.Add(connectionNumber, new KeyValuePair<List<byte>, StateObject>(new List<byte>(), stateObject));
                        stateObject.connectionNumber = connectionNumber++;

                        stateObject.workSocket = handler;

                        byteState = new KeyValuePair<List<byte>, StateObject>(null, stateObject);
                        acceptDone.Set();
                        break;

                    case PROCESS_STATE.READ:
                        //catch when client disconnects

                        //wait if accept is being called
                        //acceptDone.WaitOne();
                        try
                        {
                            stateObject = ar.AsyncState as StateObject;
                            // Read data from the client socket. 
                            bytesRead = stateObject.workSocket.EndReceive(ar);

                            if (bytesRead > 0)
                            {
                                byteState = connectionDict[stateObject.connectionNumber];

                                buffer = new byte[bytesRead];
                                Array.Copy(byteState.Value.buffer, buffer, bytesRead);

                                byteState.Key.AddRange(buffer);
                            }
                            //only put one instance of connection number into fifo
                            if (!fifo.Contains(byteState.Value.connectionNumber))
                            {

                                fifo.Add(byteState.Value.connectionNumber);
                            }
                        }
                        catch (Exception ex)
                        {
                            //will get here if client disconnects
                            fifo.RemoveAll(x => x == byteState.Value.connectionNumber);
                            connectionDict.Remove(byteState.Value.connectionNumber);
                            byteState = new KeyValuePair<List<byte>, StateObject>(new List<byte>(), null);
                        }
                        break;

                    case PROCESS_STATE.PROCESS:
                        if (fifo.Count > 0)
                        {
                            //get message from working buffer
                            //unpack will later delete message
                            //remove connection number from fifo
                            // the list in the key in known as the working buffer
                            byteState = new KeyValuePair<List<byte>, StateObject>(connectionDict[fifo[0]].Key, connectionDict[fifo[0]].Value);
                            fifo.RemoveAt(0);
                            //put a valid value in fifoCount so -1 below can be detected.
                            byteState.Value.fifoCount = fifo.Count;
                        }
                        else
                        {
                            //getting here is normal when there is no more work to be performed
                            //set fifocount to zero so rest of code know fifo was empty so code waits for next receive message
                            byteState = new KeyValuePair<List<byte>, StateObject>(null, new StateObject() { fifoCount = -1 });
                        }
                        break;

                    case PROCESS_STATE.UNPACK:
                        try
                        {
                            working_buffer = connectionDict[unpackConnectionNumber].Key;
                            workingBufferLen = working_buffer[2];
                            if ((working_buffer[0] != 0x78) && (working_buffer[1] != 0x78) && (working_buffer[workingBufferLen + 3] != 0x0D) && (working_buffer[workingBufferLen + 4] != 0x0A))
                            {



                                working_buffer.Clear();
                                return new KeyValuePair<List<byte>, StateObject>(new List<byte>(), null);
                            }
                            List<byte> packet = working_buffer.GetRange(0, workingBufferLen + 5);
                            working_buffer.RemoveRange(0, workingBufferLen + 5);
                            byteState = new KeyValuePair<List<byte>, StateObject>(packet, null);
                        }
                        catch (Exception ex)
                        {

                            int testPoint = 0;
                        }

                        break;
                }// end switch
            }

            return byteState;
        }

        static void Send(Socket handler, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }
        static void Send(Socket socket, byte[] data)
        {
            // Convert the string data to byte data using ASCII encoding.
          //   byte[] byteData = Encoding.ASCII.GetBytes("78 78 11 01 03 51 60 80 80 77 92 88 22 03 32 01 01 AA 53 36 0D 0A");

            // Begin sending the data to the remote device.
            socket.BeginSend(data, 0, data.Length, 0,
                new AsyncCallback(SendCallback), socket);
        }
        static void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket handler = ar.AsyncState as Socket;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
                // Console.WriteLine("Sent {0} bytes to client.", bytesSent);

            }
            catch (Exception e)
            {
                // Console.WriteLine(e.ToString());
                int myerror = -1;
            }
        }

        public static void AcceptCallback(IAsyncResult ar)
        {
            try
            {

                // Get the socket that handles the client request.
                // Retrieve the state object and the handler socket
                // from the asynchronous state object.

                Socket listener = (Socket)ar.AsyncState;
                Socket handler = listener.EndAccept(ar);

                // Create the state object.
                StateObject state = ReadWrite(PROCESS_STATE.ACCEPT, handler, ar, -1).Value;

                handler.BeginReceive(state.buffer, 0, BUFFER_SIZE, 0,
                    new AsyncCallback(ReadCallback), state);

            }
            catch (Exception ex)
            {
                int myerror = -1;
            }
        }

        public static void ReadCallback(IAsyncResult ar)
        {
            try
            {
                StateObject state = ar.AsyncState as StateObject;
                Socket handler = state.workSocket;

                // Read data from the client socket. 
                KeyValuePair<List<byte>, StateObject> byteState = ReadWrite(PROCESS_STATE.READ, handler, ar, -1);

                if (byteState.Value != null)
                {
                    allDone.Set();
                    handler.BeginReceive(state.buffer, 0, BUFFER_SIZE, 0,
                        new AsyncCallback(ReadCallback), state);
                }
                else
                {
                    int testPoint = 0;
                }
            }
            catch (Exception ex)
            {
                int myerror = -1;
            }

            // Signal the main thread to continue.  
            allDone.Set();
        }
    }
}


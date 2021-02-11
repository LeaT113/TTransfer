using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace TTransfer.Network
{
    public enum TTInstruction
    {
        Empty = 0,

        Discovery_Hello = 10,
        Discovery_Present = 11,
        Discovery_Bye = 12,

        Connection_RefuseBusy = 20,
        Connection_RefuseDeny = 21,
        Connection_AskPass = 22,
        Connection_SendPass = 23,
        Connection_RefusePass = 24,
        Connection_AcceptPass = 25,
        Connection_Accept = 26,

        Transfer_TransferInfo = 30,
        Transfer_FileInfo = 31,
        Transfer_FolderInfo = 32,
    }



    static class TTNet
    {
        public const int TimePasswordLength = 32;


        // General
        /// <summary>
        /// Returns true if data in buffer is in correct format, unpacks mac, instruction and data into variables.
        /// </summary>
        /// <param name="buffer">Data received by client</param>
        /// <param name="macAddress">MAC Address of remote host</param>
        /// <param name="instruction">Received TTransfer network instruction</param>
        /// <param name="data">Data remaining in buffer</param>
        /// <returns></returns>
        public static bool UnpackBuffer(byte[] buffer, ref PhysicalAddressSerializable macAddress, ref TTInstruction instruction, ref List<byte> data)
        {
            List<byte> b = new List<byte>(buffer);


            if (b.Count < 7 || !Enum.IsDefined(typeof(TTInstruction), instruction))
            {
                return false;
            }


            macAddress = new PhysicalAddressSerializable(b.GetRange(0, 6).ToArray());
            instruction = (TTInstruction)b[6];
            data = b.GetRange(7, b.Count - 7);

            return true;
        }

        /// <summary>
        /// Gets all network interfaces that are usable for communication.
        /// </summary>
        public static NetworkInterface[] GetUsableNetworkInterfaces()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(u => u.OperationalStatus == OperationalStatus.Up && u.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(u => u.GetIPProperties().UnicastAddresses.Where(i => i.Address.AddressFamily == AddressFamily.InterNetwork).Select(i => i.Address).FirstOrDefault() != null)
                .ToArray();
        }

        // UDP
        /// <summary>
        /// Calculates the network broadcast address.
        /// </summary>
        /// <param name="address">IPv4 address of the network interface</param>
        /// <param name="mask">IPv4 mask of the network interface</param>
        /// <returns></returns>
        public static IPAddress GetBroadcastAddress(IPAddress address, IPAddress mask)
        {
            uint ipAddress = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
            uint ipMaskV4 = BitConverter.ToUInt32(mask.GetAddressBytes(), 0);
            uint broadCastIpAddress = ipAddress | ~ipMaskV4;

            return new IPAddress(BitConverter.GetBytes(broadCastIpAddress));
        }

        /// <summary>
        /// Creates a presence buffer with device's Mac address, username and device type.
        /// </summary>
        public static void GeneratePresenceBuffer(PhysicalAddressSerializable mac, out byte[] buffer, TTInstruction instruction, string name = "", DeviceType type = DeviceType.Unknown)
        {
            if((int)instruction < 10 || (int)instruction > 12)
                new Exception("Presence buffer instruction out of range: " + instruction.ToString());

            var macBytes = mac.GetAddressBytes();

            if(instruction == TTInstruction.Discovery_Bye)
            {
                buffer = new byte[7];
                Array.Copy(macBytes, buffer, 6);
                buffer[6] = (byte)instruction;
            }
            else
            {
                var deviceTypeByte = (byte)type;
                var nameBytes = Encoding.UTF8.GetBytes(Properties.Settings.Default.Name);

                buffer = new byte[7+1+nameBytes.Length];
                Array.Copy(macBytes, buffer, 6);
                buffer[6] = (byte)instruction;
                buffer[7] = deviceTypeByte;
                Array.Copy(nameBytes, 0, buffer, 8, nameBytes.Length);
            }
        }


        // TCP
        public static bool UnpackTCPBuffer(byte[] buffer, ref TTInstruction instruction, ref byte[] data)
        {
            if (buffer.Length < 1 || !Enum.IsDefined(typeof(TTInstruction), instruction))
            {
                return false;
            }

            instruction = (TTInstruction)buffer[0];
            data = new byte[buffer.Length - 1];
            Array.Copy(buffer, 1, data, 0, buffer.Length - 1); // TODO Use buffer.Skip(1).ToArray()

            return true;
        }
        public static bool UnpackTCPBuffer(byte[] buffer, ref TTInstruction instruction, ref List<byte> data)
        {
            if (buffer.Length < 1 || !Enum.IsDefined(typeof(TTInstruction), instruction))
            {
                return false;
            }

            instruction = (TTInstruction)buffer[0];
            data = buffer.Skip(1).ToList();

            return true;
        }

        /// <summary>
        /// Creates a byte array from the first 4 letters of the device name and a time string.
        /// </summary>
        public static byte[] GenerateTimePasswordBytes()
        {
            return Encoding.UTF8.GetBytes(Settings.SettingsData.Name.Substring(0, 4) + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"));
        }
        public static bool CheckTimePasswordValid(byte[] passwordBytes, Device checkedDevice, int maxDifferenceMs)
        {
            
            string passString = Encoding.UTF8.GetString(passwordBytes);
            if (passString.Length < 4 + 19)
                return false;

            if (passString.Substring(0, 4) != checkedDevice.Name.Substring(0, 4))
                return false;

            string str = passString.Substring(4, passString.Length - 4);
            double difMs = DateTime.UtcNow.Subtract(DateTime.Parse(str)).TotalMilliseconds;

            
            return difMs < maxDifferenceMs;
        }
    }
}

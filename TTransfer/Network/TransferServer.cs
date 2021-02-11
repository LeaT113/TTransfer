using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CavemanTcp;

namespace TTransfer.Network
{
    class TransferServer
    {
        // Public
        public bool IsBusy { get { return clientDevice != null; } }

        // Events
        public Action<string, Console.ConsoleMessageType> OnRecordableEvent;

        // Internal
        CavemanTcpServer server;
        Device clientDevice;
        DataEncryptor clientEncryptor;
        string clientIpPort;
        int maxPingMs;
        int maxBufferSize;
        long bytesReceived;



        public TransferServer(IPAddress systemIP, int port, int maxPingMs, int maxBufferSize)
        {
            this.maxPingMs = maxPingMs;
            this.maxBufferSize= maxBufferSize;

            server = new CavemanTcpServer(systemIP.ToString(), port);
            server.Events.ClientConnected += Events_ClientConnected;
            server.Events.ClientDisconnected += Events_ClientDisconnected;
        }



        public void Start()
        {
            server.Start();
        }
        public void Stop()
        {
            server.Stop();
            server.Dispose();
        }


        // Connection
        private async Task HandleConnection(string ipPort)
        {
            // Establish connection
            bool success = await EstablishConnection(ipPort);
            if(!success)
            {
                TerminateConnection(ipPort);
                return;
            }


            // Receive data
            bytesReceived = 0;
            await ReceiveData();


            // End connection
            TerminateConnection(ipPort);
        }
        private async Task<bool> EstablishConnection(string ipPort)
        {
            return await Task.Run(() =>
            {
                if (ipPort == null || ipPort == "")
                    return false;

                string[] ipParts = ipPort.Split(':');
                if (ipParts.Length != 2)
                    return false;

                IPAddress deviceIP;
                if (!IPAddress.TryParse(ipParts[0], out deviceIP))
                    return false;

                Device remoteDevice = Settings.SettingsData.GetDevice(deviceIP);
                if (remoteDevice == null)
                    return false;


                TTInstruction instruction = TTInstruction.Empty;
                byte[] data = null;
                byte[] buffer = null;


                // Check busy
                if (IsBusy)
                {
                    OnRecordableEvent($"Connection from {remoteDevice.Name} refused because busy.", Console.ConsoleMessageType.Common);
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefuseBusy });
                    return false;
                }


                // Check permission
                if (remoteDevice.ReceiveMode == DeviceReceiveMode.Deny)
                {
                    OnRecordableEvent($"Connection from {remoteDevice.Name} refused because it's not allowed.", Console.ConsoleMessageType.Common);
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefuseDeny });
                    return false;
                }
                if(remoteDevice.ReceiveMode == DeviceReceiveMode.AskEachTime)
                {
                    // TODO Ask for permission with YesNo
                }


                // Encryption
                DataEncryptor encryptor = null;
                if(remoteDevice.EncryptionEnabled)
                {
                    encryptor = new DataEncryptor(remoteDevice.EncryptionPassword);


                    // Ask for password
                    if (!TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_AskPass }))
                        return false;


                    // Receive password
                    if (!TryRead(ipPort, 1 + TTNet.TimePasswordLength, ref instruction, ref data))
                        return false;
                    if(instruction != TTInstruction.Connection_SendPass)
                    {
                        if (instruction == TTInstruction.Connection_RefuseDeny)
                            OnRecordableEvent($"Connection from {remoteDevice.Name} refused refused because it did not have a password.", Console.ConsoleMessageType.Common);
                        
                        return false;
                    }

                    // Check password
                    byte[] receivedTimePassword = encryptor.AESDecryptBytes(data);
                    if(!TTNet.CheckTimePasswordValid(receivedTimePassword, remoteDevice, maxPingMs))
                    {
                        OnRecordableEvent($"Connection from {remoteDevice.Name} refused because it did not have the correct password.", Console.ConsoleMessageType.Common);
                        TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefusePass  });
                        return false;
                    }


                    // Send password
                    byte[] timePassword = TTNet.GenerateTimePasswordBytes();
                    buffer = new byte[1] { (byte)TTInstruction.Connection_SendPass }.ToList().Concat(encryptor.AESEncryptBytes(timePassword)).ToArray();
                    if (!TrySend(ipPort, buffer))
                        return false;
                }
                else
                {
                    // Send connection accept
                    if (!TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_Accept }))
                        return false;
                }


                // Wait if client accepted connection
                if (!TryRead(ipPort, 1, ref instruction, ref data))
                    return false;
                if (instruction != TTInstruction.Connection_Accept)
                {
                    if (instruction == TTInstruction.Connection_RefusePass)
                        OnRecordableEvent($"Connection from {remoteDevice.Name} refused because it requires a password and none is set.", Console.ConsoleMessageType.Common);

                    return false;
                }
                    


                // Check busy again
                if (IsBusy)
                {
                    OnRecordableEvent($"Refused connection from {remoteDevice.Name} because busy.", Console.ConsoleMessageType.Common);
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefuseBusy });
                    return false;
                }


                clientDevice = remoteDevice;
                clientIpPort = ipPort;
                clientEncryptor = encryptor;
                OnRecordableEvent($"Established { (clientDevice.EncryptionEnabled ? "secured" : "")} connection with {remoteDevice.Name}.", Console.ConsoleMessageType.Common);
                return true;
            });
        }
        private bool TryRead(string ipPort, int count, ref TTInstruction instruction, ref byte[] data)
        {
            ReadResult result = server.ReadWithTimeout(maxPingMs, ipPort, count);

            if (result.Status != ReadResultStatus.Success || !TTNet.UnpackTCPBuffer(result.Data, ref instruction, ref data))
            {
                return false;
            }

            return true;
        }
        private bool TrySend(string ipPort, byte[] buffer)
        {
            WriteResult result = server.SendWithTimeout(maxPingMs, ipPort, buffer);

            if (result.Status != WriteResultStatus.Success)
            {
                return false;
            }

            return true;
        }
        private void TerminateConnection(string ipPort)
        {
            // TODO Cancellation token for receiving
            if(server.GetClients().Contains(ipPort))
                server.DisconnectClient(ipPort);

            if(clientIpPort == ipPort)
            {
                clientIpPort = "";
                clientDevice = null;
                clientEncryptor = null;
            }
        }


        // Receiving
        private async Task<bool> ReceiveData()
        {
            return await Task.Run(() =>
            {
                byte[] data = null;
                TTInstruction instruction = TTInstruction.Empty;


                // Receive info about transfer
                ReadResult result = server.ReadWithTimeout(maxPingMs, clientIpPort, 13);
                if (result.Status != ReadResultStatus.Success)
                    return false;
                if (!TTNet.UnpackTCPBuffer(result.Data, ref instruction, ref data))
                    return false;


                // Receive data
                if (instruction == TTInstruction.Transfer_TransferInfo)
                {
                    // Receive files individually
                    int itemCount = BitConverter.ToInt32(data, 0);
                    long totalFileSize = BitConverter.ToInt64(data, 4);

                    for (int i = 0; i < itemCount; i++)
                    {
                        if (!ReceiveItem())
                            return false;
                    }
                }
                else
                    return false;

                OnRecordableEvent("Received successfully.", Console.ConsoleMessageType.Common);
                return true;
            });
        }

        /// <summary>
        /// Receives a stream of data for the file, returns false if there was a disrupting network problem.
        /// </summary>
        private bool ReceiveFile(string fullName, long size)
        {
            string fileName = fullName;
            int idx = fullName.LastIndexOf('\\');
            if (idx != -1)
                fileName = fullName.Substring(idx + 1);


            // Receive
            long bytesToReceive = size;
            bool useEncryption = clientEncryptor != null;
            try
            {
                using (FileStream fs = File.Create($"{Settings.SettingsData.SaveLocation}\\{fullName}"))
                {
                    ReadResult result;
                    int bufferSize = 0;
                    while (bytesToReceive > 0)
                    {
                        bufferSize = (int)Math.Min(bytesToReceive, maxBufferSize);

                        byte[] receivedBuffer = null;
                        if (useEncryption)
                        {
                            result = server.Read(clientIpPort, DataEncryptor.PredictAESLength(bufferSize));
                            if (result.Status != ReadResultStatus.Success)
                            {
                                fs.Flush();
                                return false;
                            }

                            receivedBuffer = clientEncryptor.AESDecryptBytes(result.Data);
                        }
                        else
                        {
                            result = server.Read(clientIpPort, bufferSize);
                            if (result.Status != ReadResultStatus.Success)
                            {
                                fs.Flush();
                                return false;
                            }

                            receivedBuffer = result.Data;
                        }
                        
                        fs.Write(receivedBuffer, 0, bufferSize);
                        bytesToReceive -= bufferSize;
                        bytesReceived += bufferSize;
                    }
                    fs.Flush();
                }
            }
            catch (PathTooLongException e)
            {
                OnRecordableEvent($"Could not receive '{fileName}' because the path to save is too long ({Settings.SettingsData.SaveLocation}\\{fileName})", Console.ConsoleMessageType.Error);
                return true;
            }


            return true;
        }
        private bool ReceiveFolder(string fullName)
        {
            try
            {
                Directory.CreateDirectory($"{Settings.SettingsData.SaveLocation}\\{fullName}");
            }
            catch(Exception e)
            {
                return false;
            }
            

            return true;
        }
        private bool ReceiveItem()
        {
            ReadResult result;
            byte[] data = null;
            TTInstruction instruction = TTInstruction.Empty;


            // Receive item info
            result = server.ReadWithTimeout(maxPingMs, clientIpPort, 512);
            if (result.Status != ReadResultStatus.Success)
                return false;
            if (!TTNet.UnpackTCPBuffer(result.Data, ref instruction, ref data) || !(instruction == TTInstruction.Transfer_FileInfo || instruction == TTInstruction.Transfer_FolderInfo))
                return false;
            long size = BitConverter.ToInt64(data, 0);
            string fullName = Encoding.UTF8.GetString(data, 8, 512 - 9).Replace("\0", string.Empty);


            // Receive item
            if (instruction == TTInstruction.Transfer_FileInfo)
            {
                ReceiveFile(fullName, size);
            }
            else
            {
                ReceiveFolder(fullName);
            }


            return true;
        }


        // Events
        private void Events_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            _ = HandleConnection(e.IpPort);
        }
        private void Events_ClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            TerminateConnection(e.IpPort);
        }
    }
}

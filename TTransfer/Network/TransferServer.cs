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
        IProgress<TransferProgressReport> transferProgress;
        string clientIpPort;
        int maxPingMs;
        int maxBufferSize;
        long bytesReceived;
        long totalBytes;
        string saveLocation;



        public TransferServer(IPAddress systemIP, int port, int maxPingMs, int maxBufferSize, IProgress<TransferProgressReport> transferProgress)
        {
            this.maxPingMs = maxPingMs;
            this.maxBufferSize= maxBufferSize;
            this.transferProgress = transferProgress;

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
            saveLocation = Settings.SettingsData.SaveLocation;
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

                CommunicationResult res = null;
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
                if (remoteDevice.ReceiveMode == DeviceReceiveMode.AskEachTime)
                {
                    // TODO Ask for permission with YesNo
                }


                // Encryption
                DataEncryptor encryptor = null;
                if (remoteDevice.EncryptionEnabled)
                {
                    encryptor = new DataEncryptor(remoteDevice.EncryptionPassword);


                    // Ask for password
                    if (!TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_AskPass }))
                        return false;


                    // Receive password
                    res = TryRead(ipPort, 1 + TTNet.TimePasswordLength, false);
                    if (!res.Success)
                        return false;
                    if (res.Instruction != TTInstruction.Connection_SendPass)
                    {
                        if (res.Instruction == TTInstruction.Connection_RefuseDeny)
                            OnRecordableEvent($"Connection from {remoteDevice.Name} refused refused because it did not have a password.", Console.ConsoleMessageType.Common);

                        return false;
                    }


                    // Check password
                    byte[] receivedTimePassword = encryptor.AESDecryptBytes(res.Data);
                    if (!TTNet.CheckTimePasswordValid(receivedTimePassword, remoteDevice, maxPingMs))
                    {
                        OnRecordableEvent($"Connection from {remoteDevice.Name} refused because it did not have the correct password.", Console.ConsoleMessageType.Common);
                        TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefusePass });
                        return false;
                    }


                    // Send password
                    byte[] timePassword = TTNet.GenerateTimePasswordBytes();
                    buffer = new byte[1] { (byte)TTInstruction.Connection_SendPass }
                    .ToList()
                    .Concat(encryptor.AESEncryptBytes(timePassword))
                    .ToArray();
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
                res = TryRead(ipPort, 1, false);
                if (!res.Success)
                    return false;
                if (res.Instruction != TTInstruction.Connection_Accept)
                {
                    if (res.Instruction == TTInstruction.Connection_RefusePass)
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
                OnRecordableEvent($"Established { (clientDevice.EncryptionEnabled ? "secure " : "")}connection with {remoteDevice.Name}.", Console.ConsoleMessageType.Common);
                return true;
            });
        }

        private bool TrySend(string ipPort, byte[] buffer)
        {
            WriteResult result =  server.SendWithTimeout(maxPingMs, ipPort, buffer);

            if (result.Status != WriteResultStatus.Success)
            {
                return false;
            }

            return true;
        }
        private CommunicationResult TryRead(string ipPort, int count, bool doEncryption)
        {
            ReadResult result = server.ReadWithTimeout(maxPingMs, ipPort, count);

            TTInstruction ins = TTInstruction.Empty;
            byte[] buffer = null;
            if (result.Status != ReadResultStatus.Success)
            {
                return new CommunicationResult();
            }

            if (doEncryption && clientEncryptor != null)
                buffer = clientEncryptor.AESDecryptBytes(result.Data);
            else
                buffer = result.Data;

            byte[] dat = null;
            if (!TTNet.UnpackTCPBuffer(buffer, ref ins, ref dat))
                return new CommunicationResult();

            return new CommunicationResult(ins, dat);
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

        // TODO rework bool structure as throwing exceptions
        // Receiving
        private async Task<bool> ReceiveData()
        {
            return await Task.Run(() =>
            {
                // Receive info about transfer
                int length = 13;
                if (clientEncryptor != null)
                    length = DataEncryptor.PredictAESLength(length);
                CommunicationResult res = TryRead(clientIpPort, length, true);




                // Receive data
                if (res.Instruction == TTInstruction.Transfer_TransferInfo)
                {
                    // Receive files individually
                    int itemCount = BitConverter.ToInt32(res.Data, 0);
                    totalBytes = BitConverter.ToInt64(res.Data, 4);

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
        private bool ReceiveItem()
        {
            CommunicationResult res;


            // Receive item info
            int length = 512;
            if (clientEncryptor != null)
                length = DataEncryptor.PredictAESLength(length);
            res = TryRead(clientIpPort, length, true);
            if (!res.Success)
                return false;
            if (!(res.Instruction == TTInstruction.Transfer_FileInfo || res.Instruction == TTInstruction.Transfer_FolderInfo))
                return false;
            long size = BitConverter.ToInt64(res.Data, 0);
            string fullName = Encoding.UTF8.GetString(res.Data, 8, 512 - 9).Replace("\0", string.Empty);


            // Receive item
            if (res.Instruction == TTInstruction.Transfer_FileInfo)
            {
                ReceiveFile(fullName, size);
            }
            else
            {
                ReceiveFolder(fullName);
            }


            return true;
        }
        private bool ReceiveFile(string fullName, long size)
        {
            string fileName = fullName;
            int idx = fullName.LastIndexOf('\\');
            if (idx != -1)
                fileName = fullName.Substring(idx + 1);


            // Receive
            long bytesToReceive = size;
            bool useEncryption = clientEncryptor != null;
            TransferProgressReport report = new TransferProgressReport();
            report.TotalBytes = totalBytes;
            report.ActiveItem = fileName;
            report.CurrentBytes = 0;
            report.IsSender = false;
            transferProgress.Report(report);
            try
            {
                using (FileStream fs = File.Create($"{saveLocation}\\{fullName}"))
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

                        if (bytesReceived >= report.CurrentBytes + (totalBytes / 100) || bytesToReceive == 0)
                        {
                            report.CurrentBytes = bytesReceived;
                            transferProgress.Report(report);
                        }
                    }
                    fs.Flush();
                }
            }
            catch (PathTooLongException e)
            {
                OnRecordableEvent($"Could not receive '{fileName}' because the path to save is too long ({saveLocation}\\{fileName})", Console.ConsoleMessageType.Error);
                return true;
            }


            return true;
        }
        private bool ReceiveFolder(string fullName)
        {
            try
            {
                Directory.CreateDirectory($"{saveLocation}\\{fullName}");
            }
            catch(Exception e)
            {
                return false;
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

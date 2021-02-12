using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CavemanTcp;
using TTransfer.Explorer;

namespace TTransfer.Network
{
    class TransferClient
    {
        // Public
        public bool IsBusy { get { return serverDevice != null; } }

        // Events
        public Action<string, Console.ConsoleMessageType> OnRecordableEvent;

        // Internal
        CavemanTcpClient client;
        List<DirectoryItem> items;
        Device serverDevice;
        DataEncryptor serverEncryptor;
        IProgress<TransferProgressReport> transferProgress;
        int port;
        int maxPingMs;
        long maxBufferSize;
        long bytesSent;
        long totalBytes;
        


        public TransferClient(int port, int maxPingMs, int maxBufferSize, IProgress<TransferProgressReport> transferProgress)
        {
            this.port = port;
            this.maxPingMs = maxPingMs;
            this.maxBufferSize = maxBufferSize;
            this.transferProgress = transferProgress;
        }



        public void StartTransfer(List<DirectoryItem> itemsToSend, Device device)
        {
            if (device == null)
                return;

            if (IsBusy)
                return;


            // Copy list to prevent changes while sending
            items = itemsToSend.Select(item => (DirectoryItem)item.Clone()).ToList();


            // Create connection
            client = new CavemanTcpClient(device.IPAddress.ToString(), port);
            serverDevice = device;

            client.Events.ClientConnected += Events_ClientConnected;
            client.Events.ClientDisconnected += Events_ClientDisconnected;
            client.Connect(Settings.SettingsData.MaxNetworkPingMs);

            // TODO Add timeout
        }


        // Connection
        private async Task HandleConnection() 
        {
            // Establish connection
            bool success = await EstablishConnection();
            if (!success)
            {
                TerminateConnection();
                return;
            }


            // Send data
            success = await SendData();
            if (success)
                OnRecordableEvent("Sent all data successfully.", Console.ConsoleMessageType.Common);
            else
                OnRecordableEvent("There was an error while sending data.", Console.ConsoleMessageType.Error);


            // End connection
            TerminateConnection();
        }
        private async Task<bool> EstablishConnection()
        {
            return await Task.Run(() =>
            {
                CommunicationResult res = null;
                byte[] buffer = null;


                // Deny, accept, or askpass
                res = TryRead(1, maxPingMs + Settings.SettingsData.MaxPermissionAskWaitMs);
                if (!res.Success)
                    return false;


                // Denied
                switch (res.Instruction)
                {
                    case TTInstruction.Connection_RefuseBusy:
                        OnRecordableEvent($"Connection to {serverDevice.Name} failed because it's busy right now.", Console.ConsoleMessageType.Error);
                        return false;


                    case TTInstruction.Connection_RefuseDeny:
                        OnRecordableEvent($"Connection to {serverDevice.Name} failed because it hasn't allowed you to send them files.", Console.ConsoleMessageType.Error);
                        return false;
                }


                // Encryption
                DataEncryptor encryptor = null;
                if (serverDevice.EncryptionEnabled)
                {
                    // Check if server also has password set
                    if (res.Instruction != TTInstruction.Connection_AskPass)
                    {
                        OnRecordableEvent($"Connection to {serverDevice.Name} refused because you have set a password and it has none.", Console.ConsoleMessageType.Error);
                        TrySend(new byte[] { (byte)TTInstruction.Connection_RefusePass }, false);
                        return false;
                    }


                    // Send password
                    encryptor = new DataEncryptor(serverDevice.EncryptionPassword);
                    byte[] timePassword = TTNet.GenerateTimePasswordBytes();
                    buffer = new byte[1] { (byte)TTInstruction.Connection_SendPass }
                    .ToList()
                    .Concat(encryptor.AESEncryptBytes(timePassword))
                    .ToArray();
                    if (!TrySend(buffer, false))
                        return false;


                    // Receive password
                    res = TryRead(TTNet.TimePasswordLength + 1);
                    if (!res.Success)
                        return false;


                    // Check password
                    byte[] receivedTimePassword = encryptor.AESDecryptBytes(res.Data);
                    if (Enumerable.SequenceEqual(timePassword, receivedTimePassword) || !TTNet.CheckTimePasswordValid(receivedTimePassword, serverDevice, maxPingMs))
                    {
                        OnRecordableEvent($"Connection to {serverDevice.Name} refused because it did not have the correct password.", Console.ConsoleMessageType.Error);
                        // TODO Send RefusePass to server
                        return false;
                    }
                }
                else
                {
                    // Check if server also doesn't use password
                    if (res.Instruction != TTInstruction.Connection_Accept)
                    {
                        OnRecordableEvent($"Connection to {serverDevice.Name} failed because it requires a password and none is set.", Console.ConsoleMessageType.Error);
                        TrySend(new byte[] { (byte)TTInstruction.Connection_RefuseDeny }, false); // TODO Doesn't work, server doesn't recognize this scenario
                        return false;
                    }
                }


                // Send connection accept
                if (!TrySend(new byte[] { (byte)TTInstruction.Connection_Accept }, false))
                    return false;


                serverEncryptor = encryptor;
                OnRecordableEvent($"Established { (serverDevice.EncryptionEnabled ? "secure " : "")}connection with {serverDevice.Name}.", Console.ConsoleMessageType.Common);
                return true;
            });
        }

        private bool TrySend(byte[] buffer, bool doEncryption)
        {
            byte[] b = buffer;
            if (doEncryption && serverEncryptor != null)
                b = serverEncryptor.AESEncryptBytes(buffer);

            WriteResult result = client.SendWithTimeout(maxPingMs, b);

            if (result.Status != WriteResultStatus.Success)
            {
                return false;
            }

            return true;
        }
        private CommunicationResult TryRead(int count, int overrideTimeoutMs = -1)
        {

            ReadResult result = client.ReadWithTimeout(overrideTimeoutMs == -1 ? maxPingMs : overrideTimeoutMs, count);

            TTInstruction ins = TTInstruction.Empty;
            byte[] dat = null;
            if (result.Status != ReadResultStatus.Success || !TTNet.UnpackTCPBuffer(result.Data, ref ins, ref dat))
            {
                return new CommunicationResult();
            }

            return new CommunicationResult(ins, dat);
        }

        public void TerminateConnection()
        {
            // TODO Cancellation token for sending

            if(client != null)
            {
                if (client.IsConnected)
                    client.Disconnect();

                client.Dispose();
                client = null;
            }

            serverEncryptor = null;
            serverDevice = null;
        }


        // Sending
        private async Task<bool> SendData()
        {
            return await Task.Run(() =>
            {
                // Send files info
                byte[] infoBuffer = new byte[13];
                infoBuffer[0] = (byte)TTInstruction.Transfer_TransferInfo;

                int totalItemCount = items.Count();
                foreach(var item in items)
                {
                    totalItemCount += item.GetTotalChildCount();
                }
                byte[] countBytes = BitConverter.GetBytes(totalItemCount);
                Array.Copy(countBytes, 0, infoBuffer, 1, 4);

                long totalSize = 0;
                foreach (var item in items)
                {
                    totalSize += item.GetTotalSize();
                }
                byte[] sizeBytes = BitConverter.GetBytes(totalSize);
                Array.Copy(sizeBytes, 0, infoBuffer, 1 + 4, 8);

                if (!TrySend(infoBuffer, true))
                    return false;


                // Send data
                bytesSent = 0;
                totalBytes = totalSize;
                foreach (var item in items)
                {
                    if (item.IsFolder)
                    {
                        if (!SendFolder(item, ""))
                            return false;
                    }
                    else
                    {
                        if (!SendFile(item, ""))
                            return false;
                    }
                }


                return true;
            });
        }
        private bool SendFile(DirectoryItem file, string relativePath)
        {
            if (file.IsFolder)
                throw new Exception("Cannot send folder as file.");


            // Send file info
            byte[] infoBuffer = new byte[1+8+1024]; // instruction + fileSize + name (max 256 chars and 4 bytes per char)
            infoBuffer[0] = (byte)TTInstruction.Transfer_FileInfo;

            byte[] sizeBytes = BitConverter.GetBytes(file.Size);
            Array.Copy(sizeBytes, 0, infoBuffer, 1, sizeBytes.Length);

            string fullName = $"{relativePath}{file.Name}";
            byte[] fullNameBytes = Encoding.UTF8.GetBytes(fullName);
            Array.Copy(fullNameBytes, 0, infoBuffer, 1 + 8, fullNameBytes.Length);


            if (!TrySend(infoBuffer, true))
                return false;


            // Send file
            long bytesToSend = file.Size;
            bool useEncryption = serverEncryptor != null;
            TransferProgressReport report = new TransferProgressReport();
            report.TotalBytes = totalBytes;
            report.ActiveItem = file.Name;
            report.CurrentBytes = 0;
            report.IsSender = true;
            transferProgress.Report(report);
            WriteResult result;
            try
            {
                using (FileStream fs = File.OpenRead(file.Path))
                {
                    int bufferSize = 0;
                    while (bytesToSend > 0)
                    {
                        bufferSize = (int)Math.Min(bytesToSend, maxBufferSize);

                        byte[] buffer = new byte[bufferSize];
                        fs.Read(buffer, 0, bufferSize);

                        if (useEncryption)
                            buffer = serverEncryptor.AESEncryptBytes(buffer);

                        result = client.Send(buffer);
                        if (result.Status != WriteResultStatus.Success)
                            return false;

                        bytesToSend -= bufferSize;
                        bytesSent += bufferSize;

                        if(bytesSent >= report.CurrentBytes + (totalBytes/100) || bytesToSend == 0)
                        {
                            report.CurrentBytes = bytesSent;
                            transferProgress.Report(report);
                        }
                    }
                    fs.Flush();
                }
            }
            catch (Exception e)
            {
                OnRecordableEvent($"Aborting, failed to send {file.Name} ({e.Message}).", Console.ConsoleMessageType.Error);
                return false;
            }


            return true;
        }
        private bool SendFolder(DirectoryItem folder, string relativePath)
        {
            if (!folder.IsFolder)
                throw new Exception("Cannot send file as folder.");


            // Send folder info
            byte[] infoBuffer = new byte[1+8+1024];
            infoBuffer[0] = (byte)TTInstruction.Transfer_FolderInfo;

            string fullName = $"{relativePath}{folder.Name}";
            byte[] fullNameBytes = Encoding.UTF8.GetBytes(fullName);
            Array.Copy(fullNameBytes, 0, infoBuffer, 1 + 8, fullNameBytes.Length);

            if (!TrySend(infoBuffer, true))
                return false;


            // Send contents
            fullName += "\\";
            DirectoryItem[] contents = folder.GetChildren();
            foreach(var c in contents)
            {
                if (c.IsFolder)
                {
                    if (!SendFolder(c, fullName))
                        return false;
                }
                else
                {
                    if(!SendFile(c, fullName))
                        return false;
                }
            }


            return true;
        }


        // Events
        private void Events_ClientConnected(object sender, EventArgs e)
        {
            OnRecordableEvent("Connected to server.", Console.ConsoleMessageType.Common);
            _ = HandleConnection();
        }
        private void Events_ClientDisconnected(object sender, EventArgs e)
        {
            OnRecordableEvent("Disconnected from server.", Console.ConsoleMessageType.Common);
            TerminateConnection();
        }
    }
}

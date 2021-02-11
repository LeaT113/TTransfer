using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        int port;
        int maxPingMs;
        long maxBufferSize;
        


        public TransferClient(int port, int maxPingMs, int maxBufferSize)
        {
            this.port = port;
            this.maxPingMs = maxPingMs;
            this.maxBufferSize = maxBufferSize;
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
                OnRecordableEvent("Received all data successfully.", Console.ConsoleMessageType.Common);
            else
                OnRecordableEvent("There was an error while receiving data.", Console.ConsoleMessageType.Error);


            // End connection
            TerminateConnection();
        }
        private async Task<bool> EstablishConnection()
        {
            return await Task.Run(() =>
            {
                TTInstruction instruction = TTInstruction.Empty;
                byte[] data = null;
                byte[] buffer = null;


                // Deny, accept, or askpass
                if (!TryRead(1, ref instruction, ref data))
                    return false;


                // Denied
                switch (instruction)
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
                if(serverDevice.EncryptionEnabled)
                {
                    // Check if server also has password set
                    if(instruction != TTInstruction.Connection_AskPass)
                    {
                        OnRecordableEvent($"Connection to {serverDevice.Name} refused because you have set a password and it has none.", Console.ConsoleMessageType.Error);
                        TrySend(new byte[] { (byte)TTInstruction.Connection_RefusePass });
                        return false;
                    }


                    // Send password
                    encryptor = new DataEncryptor(serverDevice.EncryptionPassword);
                    byte[] timePassword = TTNet.GenerateTimePasswordBytes();
                    buffer = new byte[1] { (byte)TTInstruction.Connection_SendPass }.ToList().Concat(encryptor.AESEncryptBytes(timePassword)).ToArray();
                    if (!TrySend(buffer))
                        return false;


                    // Receive password
                    if (!TryRead(TTNet.TimePasswordLength + 1, ref instruction, ref data))
                        return false;


                    // Check password
                    byte[] receivedTimePassword = encryptor.AESDecryptBytes(data);
                    if (Enumerable.SequenceEqual(timePassword, receivedTimePassword) || !TTNet.CheckTimePasswordValid(receivedTimePassword, serverDevice, maxPingMs))
                    {
                        OnRecordableEvent($"Connection to {serverDevice.Name} refused because it did not have the correct password.", Console.ConsoleMessageType.Error);
                        return false;
                    }
                }
                else
                {
                    // Check if server also doesn't use password
                    if(instruction != TTInstruction.Connection_Accept)
                    {
                        OnRecordableEvent($"Connection to {serverDevice.Name} failed because it requires a password and none is set.", Console.ConsoleMessageType.Error);
                        TrySend(new byte[] { (byte)TTInstruction.Connection_RefuseDeny }); // TODO Doesn't work, server doesn't recognize this scenario
                        return false;
                    }
                }


                // Send connection accept
                if (!TrySend(new byte[] { (byte)TTInstruction.Connection_Accept }))
                    return false;


                serverEncryptor = encryptor;
                OnRecordableEvent($"Established { (serverDevice.EncryptionEnabled ? "secured" : "")} connection with {serverDevice.Name}.", Console.ConsoleMessageType.Common);
                return true;
            });
        }
        private bool TrySend(byte[] buffer)
        {
            WriteResult result = client.SendWithTimeout(maxPingMs, buffer);

            if (result.Status != WriteResultStatus.Success)
            {
                OnRecordableEvent($"Could not send data to {serverDevice.Name}.", Console.ConsoleMessageType.Error);
                return false;
            }

            return true;
        }
        private bool TryRead(int count, ref TTInstruction instruction, ref byte[] data)
        {
            ReadResult result = client.ReadWithTimeout(maxPingMs, count);

            if (result.Status != ReadResultStatus.Success || !TTNet.UnpackTCPBuffer(result.Data, ref instruction, ref data))
            {
                OnRecordableEvent($"Timed out {serverDevice.Name}.", Console.ConsoleMessageType.Error);
                return false;
            }

            return true;
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
        // TODO Encrypt file info
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

                client.Send(infoBuffer);


                // Send data
                long totalBytesSent = 0;
                foreach (var item in items)
                {
                    if (item.IsFolder)
                    {
                        if (!SendFolder(item, "", ref totalBytesSent))
                            return false;
                    }
                    else
                    {
                        if (!SendFile(item, "", ref totalBytesSent))
                            return false;
                    }   
                }


                return true;
            });
        }

        /// <summary>
        /// Sends a file to the server and returns false if there was a network error.
        /// </summary>
        private bool SendFile(DirectoryItem file, string relativePath, ref long totalBytesSent)
        {
            if (file.IsFolder)
                throw new Exception("Cannot send folder as file.");

            WriteResult result;


            // Send file info
            byte[] b = new byte[512];
            b[0] = (byte)TTInstruction.Transfer_FileInfo;

            byte[] sizeBytes = BitConverter.GetBytes(file.Size);
            Array.Copy(sizeBytes, 0, b, 1, sizeBytes.Length);

            string fullName = $"{relativePath}{file.Name}";
            byte[] fullNameBytes = Encoding.UTF8.GetBytes(fullName);
            Array.Copy(fullNameBytes, 0, b, 1 + sizeBytes.Length, fullNameBytes.Length);

            result = client.Send(b);
            if (result.Status != WriteResultStatus.Success)
                return false;


            // Send file
            long bytesToSend = file.Size;
            bool useEncryption = serverEncryptor != null;
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

                        client.Send(buffer);
                        if (result.Status != WriteResultStatus.Success)
                            return false;

                        bytesToSend -= bufferSize;
                        totalBytesSent += bufferSize;
                    }
                    fs.Flush();
                }
            }
            catch(Exception e)
            {
                OnRecordableEvent($"Aborting, failed to send {file.Name}.", Console.ConsoleMessageType.Error);
                return false;
            }
            

            return true;
        }
        
         /// <summary>
        /// Sends a folder and all of it contents recursively to the server and returns false if there was a network error.
        /// </summary>
        private bool SendFolder(DirectoryItem folder, string relativePath, ref long totalBytes)
        {
            if (!folder.IsFolder)
                throw new Exception("Cannot send file as folder.");

            WriteResult result;


            // Send folder info
            byte[] b = new byte[512]; // TODO Expand to 1024+9 to cover all of UTF8?
            b[0] = (byte)TTInstruction.Transfer_FolderInfo;

            string fullName = $"{relativePath}{folder.Name}";
            byte[] fullNameBytes = Encoding.UTF8.GetBytes(fullName);
            Array.Copy(fullNameBytes, 0, b, 1 + 8, fullNameBytes.Length);

            result = client.Send(b);
            if (result.Status != WriteResultStatus.Success)
                return false;


            // Send contents
            fullName += "\\";
            DirectoryItem[] contents = folder.GetChildren();
            foreach(var c in contents)
            {
                if (c.IsFolder)
                {
                    if (!SendFolder(c, fullName, ref totalBytes))
                        return false;
                }
                else
                {
                    if(!SendFile(c, fullName, ref totalBytes))
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

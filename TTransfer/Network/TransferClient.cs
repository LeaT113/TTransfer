using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
        public Action TransferCompleted; // TODO Event

        // Internal
        CavemanTcpClient client;
        List<DirectoryItem> items;
        Device serverDevice;
        DataEncryptor serverEncryptor;
        IProgress<TransferProgressReport> transferProgress;
        Stopwatch transferStopwatch;
        int port;
        int maxPingMs;
        long maxBufferSize;
        long bytesSent;
        long totalBytes;
        bool terminatingConnection;



        public TransferClient(int port, int maxPingMs, int maxBufferSize, IProgress<TransferProgressReport> transferProgress)
        {
            this.port = port;
            this.maxPingMs = maxPingMs;
            this.maxBufferSize = maxBufferSize;
            this.transferProgress = transferProgress;
            terminatingConnection = false;
            this.bytesSent = 0;
        }



        // Public
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
            
            client.Keepalive.EnableTcpKeepAlives = false;
            //client.Keepalive.TcpKeepAliveInterval = 5;
            //client.Keepalive.TcpKeepAliveTime = 5;
            //client.Keepalive.TcpKeepAliveRetryCount = 5;

            client.Connect(Settings.SettingsData.MaxNetworkPingMs);

            // TODO Add timeout / what does timeout on connect do
        }


        // Communication
        private void TrySend(byte[] buffer, bool doEncryption)
        {
            byte[] b = buffer;

            if(doEncryption)
            {
                if (serverEncryptor == null)
                    throw new FailedSendingException("Encryptor is not initialized.");

                b = serverEncryptor.AESEncryptBytes(buffer);
            }


            WriteResult result = client.SendWithTimeout(maxPingMs, b);
            if (result.Status != WriteResultStatus.Success)
            {
                throw new FailedSendingException($"Could not send data ({result.Status}).");
            }
        }
        private CommunicationResult TryRead(int count, int? overrideTimeoutMs = null)
        {
            int timeout = overrideTimeoutMs ?? maxPingMs;
            ReadResult result = client.ReadWithTimeout(timeout, count);
            if (result.Status != ReadResultStatus.Success)
            {
                throw new FailedReceivingException($"Did not receive response ({result.Status}).");
            }

            TTInstruction ins = TTInstruction.Empty;
            byte[] dat = null;
            if (!TTNet.UnpackTCPBuffer(result.Data, ref ins, ref dat))
            {
                throw new FailedReceivingException($"Received invalid response.");
            }

            return new CommunicationResult(ins, dat);
        }


        // Connection
        private async Task HandleConnection() 
        {
            try
            {
                await EstablishConnection();


                await SendData();
            }
            catch (Exception e)
            {
                OnRecordableEvent(e.Message, Console.ConsoleMessageType.Error);
            }
            finally
            {
                transferProgress.Report(new TransferProgressReport(true));
                TerminateConnection();
            }
        }
        private async Task EstablishConnection()
        {
            await Task.Run(() =>
            {
                CommunicationResult res = null;
                byte[] buffer = null;


                // Deny, accept, or askpass
                res = TryRead(1, maxPingMs + Settings.SettingsData.MaxPermissionAskWaitMs);


                // Denied
                switch (res.Instruction)
                {
                    case TTInstruction.Connection_RefuseBusy:
                        throw new FailedConnectingException($"Connection to {serverDevice.Name} failed because it's busy right now.");


                    case TTInstruction.Connection_RefuseDeny:
                        throw new FailedConnectingException($"Connection to {serverDevice.Name} failed because it hasn't allowed you to send them files.");
                }


                // Encryption
                DataEncryptor encryptor = null;
                if (serverDevice.EncryptionEnabled)
                {
                    // Check if server also has password set
                    if (res.Instruction != TTInstruction.Connection_AskPass)
                    {
                        TrySend(new byte[] { (byte)TTInstruction.Connection_RefusePass }, false);
                        throw new FailedConnectingException($"Connection to {serverDevice.Name} refused because you have set a password and it has none.");
                    }


                    // Send password
                    encryptor = new DataEncryptor(serverDevice.EncryptionPassword);
                    byte[] timePassword = TTNet.GenerateTimePasswordBytes();
                    buffer = new byte[1] { (byte)TTInstruction.Connection_SendPass }.ToList().Concat(encryptor.AESEncryptBytes(timePassword)).ToArray();
                    TrySend(buffer, false);


                    // Receive password response
                    res = TryRead(1);
                    if(res.Instruction != TTInstruction.Connection_AcceptPass)
                    {
                        throw new FailedConnectingException($"Connection to {serverDevice.Name} failed because you do not have the right password.");
                    }


                    // Receive password
                    try
                    {
                        res = TryRead(TTNet.TimePasswordLength + 1);
                    }
                    catch(Exception e)
                    {
                        OnRecordableEvent("Caught " + e.Message, Console.ConsoleMessageType.Error); // TODO why is this like this
                        throw new Exception();
                    }
                    

                    // Check password
                    byte[] receivedTimePassword = encryptor.AESDecryptBytes(res.Data);
                    if (Enumerable.SequenceEqual(timePassword, receivedTimePassword) || !TTNet.CheckTimePasswordValid(receivedTimePassword, serverDevice, maxPingMs))
                    {
                        // TODO Send RefusePass to server
                        throw new FailedConnectingException($"Connection to {serverDevice.Name} refused because it did not have the correct password.");
                        
                    }
                }
                else
                {
                    // Check if server also doesn't use password
                    if (res.Instruction != TTInstruction.Connection_Accept)
                    {
                        TrySend(new byte[] { (byte)TTInstruction.Connection_RefuseDeny }, false); // TODO Doesn't work, server doesn't recognize this scenario
                        throw new FailedConnectingException($"Connection to {serverDevice.Name} failed because it requires a password and none is set.");
                    }
                }


                // Send connection accept
                TrySend(new byte[] { (byte)TTInstruction.Connection_Accept }, false);


                serverEncryptor = encryptor;
                OnRecordableEvent($"Established { (serverDevice.EncryptionEnabled ? "secure " : "")}connection with {serverDevice.Name}.", Console.ConsoleMessageType.Common);
            });
        }
        public void TerminateConnection()
        {
            OnRecordableEvent($"Terminate connection was called ({!terminatingConnection})", Console.ConsoleMessageType.Warning);
            if (terminatingConnection || client == null)
                return;

            
            terminatingConnection = true;


            // TODO Cancellation token for sending

            
            if (client != null)
            {
                client.Disconnect();
                client = null;
            }

            serverEncryptor = null;
            serverDevice = null;

            OnRecordableEvent("Connection terminated.", Console.ConsoleMessageType.Common);
            terminatingConnection = false;
        }


        // Data
        private async Task SendData()
        {
            transferStopwatch = new Stopwatch();
            transferStopwatch.Start();
            int totalItemCount = 0;
            await Task.Run(() =>
            {
                // Send files info
                byte[] infoBuffer = new byte[13];
                infoBuffer[0] = (byte)TTInstruction.Transfer_TransferInfo;

                totalItemCount = items.Count();
                foreach (var item in items)
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

                TrySend(infoBuffer, serverEncryptor != null);


                // Send data
                bytesSent = 0;
                totalBytes = totalSize;
                foreach (var item in items)
                {
                    if (item.IsFolder)
                    {
                        SendFolder(item, "");
                    }
                    else
                    {
                        SendFile(item, "");
                    }
                }
            });


            transferStopwatch.Stop();
            OnRecordableEvent($"Sucessfully sent {totalItemCount} item/s ({ExplorerControl.FormatFileSize(bytesSent)}) in {TTNet.FormatTimeSpan(transferStopwatch.Elapsed)} at average { ExplorerControl.FormatFileSize(totalBytes * 1000 / transferStopwatch.ElapsedMilliseconds)}/s.", Console.ConsoleMessageType.Common);
        }
        private void SendFile(DirectoryItem file, string relativePath)
        {
            if (file.IsFolder)
                throw new FailedSendingException("Cannot send folder as file.");


            // Send file info
            byte[] infoBuffer = new byte[1 + 8 + 1024]; // instruction + fileSize + name (max 256 chars and 4 bytes per char)
            infoBuffer[0] = (byte)TTInstruction.Transfer_FileInfo;

            byte[] sizeBytes = BitConverter.GetBytes(file.Size);
            Array.Copy(sizeBytes, 0, infoBuffer, 1, sizeBytes.Length);

            string fullName = $"{relativePath}{file.Name}";
            byte[] fullNameBytes = Encoding.UTF8.GetBytes(fullName);
            Array.Copy(fullNameBytes, 0, infoBuffer, 1 + 8, fullNameBytes.Length);

            TrySend(infoBuffer, serverEncryptor != null);


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
            using (FileStream fs = File.OpenRead(file.Path))
            {
                if (fs == null)
                    throw new FailedSendingException($"Could not read file at '{file.Path}'.");

                int bufferSize = 0;
                while (bytesToSend > 0)
                {
                    try
                    {
                        // TODO What if I dont segment it manually, but let TCP do it (speed?, what about fail in the middle)
                        bufferSize = (int)Math.Min(bytesToSend, maxBufferSize);
                        byte[] buffer = new byte[bufferSize];
                        fs.Read(buffer, 0, bufferSize);


                        if (useEncryption)
                        {
                            if (serverEncryptor == null)
                                throw new FailedSendingException("The encryptor is not initialized.");

                            buffer = serverEncryptor.AESEncryptBytes(buffer);
                        }


                        result = client.Send(buffer);
                        if (result.Status != WriteResultStatus.Success)
                            throw new FailedSendingException("Send/write operation was not successful.");
                    }
                    catch(Exception e)
                    {
                        fs.Flush();
                        throw new FailedSendingException($"Failed while sending '{file.Name}' ({e.Message}).");
                    }
                    

                    bytesToSend -= bufferSize;
                    bytesSent += bufferSize;

                    
                    if (bytesSent >= report.CurrentBytes + (totalBytes / 100) || bytesToSend == 0)
                    {
                        report.CurrentBytes = bytesSent;
                        transferProgress.Report(report);
                    }

                    if(Settings.SettingsData.SlowSending)
                        Thread.Sleep(1); // Fix for network crash
                }
                fs.Flush();
            }
        }
        private void SendFolder(DirectoryItem folder, string relativePath)
        {
            if (!folder.IsFolder)
                throw new FailedSendingException("Cannot send file as folder.");


            // Send folder info
            byte[] infoBuffer = new byte[1+8+1024];
            infoBuffer[0] = (byte)TTInstruction.Transfer_FolderInfo;

            string fullName = $"{relativePath}{folder.Name}";
            byte[] fullNameBytes = Encoding.UTF8.GetBytes(fullName);
            Array.Copy(fullNameBytes, 0, infoBuffer, 1 + 8, fullNameBytes.Length);

            TrySend(infoBuffer, serverEncryptor != null);


            // Send contents
            fullName += "\\";
            DirectoryItem[] contents = folder.GetChildren();
            foreach(var c in contents)
            {
                if (c.IsFolder)
                {
                    SendFolder(c, fullName);
                }
                else
                {
                    SendFile(c, fullName);
                }
            }
        }


        // Events
        private void Events_ClientConnected(object sender, EventArgs e)
        {
            OnRecordableEvent("Connected to server.", Console.ConsoleMessageType.Common);
            _ = HandleConnection();
        }
        private void Events_ClientDisconnected(object sender, EventArgs e)
        {
            TerminateConnection();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;
using System.Threading.Tasks;
using CavemanTcp;
using System.Windows;
using System.Diagnostics;

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
        Settings.ConfirmationDialog confirmationDialog;
        Timer timer;
        Stopwatch transferStopwatch;
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

            server.Keepalive.EnableTcpKeepAlives = true;
            server.Keepalive.TcpKeepAliveInterval = 5;
            server.Keepalive.TcpKeepAliveTime = 5;
            server.Keepalive.TcpKeepAliveRetryCount = 5;
        }



        // Public
        public void Start()
        {
            server.Start();
        }
        public void Stop()
        {
            server.Stop();
            server.Dispose();
            server = null;
        }


        // Communication
        private void TrySend(string ipPort, byte[] buffer)
        {
            WriteResult result = server.SendWithTimeout(maxPingMs, ipPort, buffer);

            if (result.Status != WriteResultStatus.Success)
            {
                throw new FailedSendingException($"Could not send data ({result.Status}).");
            }
        }
        private CommunicationResult TryRead(string ipPort, int count, bool doEncryption)
        {
            ReadResult result = server.ReadWithTimeout(maxPingMs, ipPort, count);

            TTInstruction ins = TTInstruction.Empty;
            byte[] buffer = null;
            if (result.Status != ReadResultStatus.Success)
            {
                throw new FailedReceivingException($"Did not receive response ({result.Status}).");
            }

            if (doEncryption && clientEncryptor != null)
                buffer = clientEncryptor.AESDecryptBytes(result.Data);
            else
                buffer = result.Data;

            byte[] dat = null;
            if (!TTNet.UnpackTCPBuffer(buffer, ref ins, ref dat))
                throw new FailedReceivingException($"Received invalid response.");

            return new CommunicationResult(ins, dat);
        }


        // Connection
        private async Task HandleConnection(string ipPort)
        {
            // Establish connection
            try
            {
                await EstablishConnection(ipPort);


                bytesReceived = 0;
                saveLocation = Settings.SettingsData.SaveLocation;
                await ReceiveData();
                
            }
            catch(Exception e)
            {
                OnRecordableEvent(e.Message, Console.ConsoleMessageType.Warning);
            }
            finally
            {
                transferProgress.Report(new TransferProgressReport(true));
                TerminateConnection(ipPort);
            }
        }
        private async Task EstablishConnection(string ipPort)
        {
            await Task.Run(() =>
            {
                if (ipPort == null || ipPort == "")
                    throw new FailedConnectingException("Wrong IP.");

                string[] ipParts = ipPort.Split(':');
                if (ipParts.Length != 2)
                    throw new FailedConnectingException("Wrong IP.");

                IPAddress deviceIP;
                if (!IPAddress.TryParse(ipParts[0], out deviceIP))
                    throw new FailedConnectingException("Unknown IP.");

                Device remoteDevice = Settings.SettingsData.GetDevice(deviceIP);
                if (remoteDevice == null)
                    throw new FailedConnectingException("Unknown IP.");


                CommunicationResult res = null;
                byte[] buffer = null;


                // Check busy
                if (IsBusy)
                {
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefuseBusy });
                    throw new FailedConnectingException($"Connection from {remoteDevice.Name} refused because busy.");
                }


                // Check permission
                if (remoteDevice.ReceiveMode == DeviceReceiveMode.Deny)
                {   
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefuseDeny });
                    throw new FailedConnectingException($"Connection from {remoteDevice.Name} refused because it's not allowed.");
                }
                if (remoteDevice.ReceiveMode == DeviceReceiveMode.AskEachTime)
                {
                    timer = new Timer(Settings.SettingsData.MaxPermissionAskWaitMs);
                    timer.Elapsed += Events_TimerPermissionAskTimeout;
                    timer.Start();

                    bool allowed = false;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        confirmationDialog = new Settings.ConfirmationDialog("Accept connection?", $"Do you want to accept a connection from '{remoteDevice.Name}'?", "Yes", "No");
                        confirmationDialog.Owner = Application.Current.MainWindow;
                        allowed = confirmationDialog.ShowDialog() ?? false;
                    });
                    timer.Stop();
                    timer.Dispose();
                    if (!allowed)
                    {
                        timer.Stop();
                        timer.Dispose();

                        
                        TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefuseDeny });
                        throw new FailedConnectingException($"Connection from {remoteDevice.Name} refused because you refused it.");
                    }
                }


                // Encryption
                DataEncryptor encryptor = null;
                if (remoteDevice.EncryptionEnabled)
                {
                    encryptor = new DataEncryptor(remoteDevice.EncryptionPassword);


                    // Ask for password
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_AskPass });


                    // Receive password
                    res = TryRead(ipPort, 1 + TTNet.TimePasswordLength, false);
                    if (res.Instruction != TTInstruction.Connection_SendPass)
                    {
                        if (res.Instruction == TTInstruction.Connection_RefuseDeny)
                            throw new FailedConnectingException($"Connection from {remoteDevice.Name} refused refused because it did not have a password.");
                    }


                    // Check password
                    byte[] receivedTimePassword = encryptor.AESDecryptBytes(res.Data);
                    if (!TTNet.CheckTimePasswordValid(receivedTimePassword, remoteDevice, maxPingMs))
                    {
                        TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefusePass });
                        throw new FailedConnectingException($"Connection from {remoteDevice.Name} refused because it did not have the correct password.");
                    }
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_AcceptPass });


                    // Send password
                    byte[] timePassword = TTNet.GenerateTimePasswordBytes();
                    buffer = new byte[1] { (byte)TTInstruction.Connection_SendPass }.ToList().Concat(encryptor.AESEncryptBytes(timePassword)).ToArray();
                    TrySend(ipPort, buffer);
                }
                else
                {
                    // Send connection accept
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_Accept });
                }


                // Wait if client accepted connection
                res = TryRead(ipPort, 1, false);
                if (res.Instruction != TTInstruction.Connection_Accept)
                {
                    if (res.Instruction == TTInstruction.Connection_RefusePass)
                        throw new FailedConnectingException($"Connection from {remoteDevice.Name} refused because it requires a password and none is set.");
                }


                // Check busy again
                if (IsBusy)
                {
                    TrySend(ipPort, new byte[] { (byte)TTInstruction.Connection_RefuseBusy });
                    throw new FailedConnectingException($"Refused connection from {remoteDevice.Name} because busy.");
                }


                clientDevice = remoteDevice;
                clientIpPort = ipPort;
                clientEncryptor = encryptor;
                OnRecordableEvent($"Established { (clientDevice.EncryptionEnabled ? "secure " : "")}connection with {remoteDevice.Name}.", Console.ConsoleMessageType.Common);
            });
        }
        private void TerminateConnection(string ipPort)
        {
            if (ipPort == "")
                return;


            // TODO Cancellation token for receiving


            if(server.GetClients().Contains(ipPort))
                server.DisconnectClient(ipPort);

            if(clientIpPort == ipPort)
            {
                clientIpPort = "";
                clientDevice = null;
                clientEncryptor = null;

                OnRecordableEvent("Connection terminated.", Console.ConsoleMessageType.Common);
            }
        }


        // Data
        private async Task ReceiveData()
        {
            transferStopwatch = new Stopwatch();
            transferStopwatch.Start();
            await Task.Run(() =>
            {
                // Receive info about transfer
                int length = 13;
                if (clientEncryptor != null)
                    length = DataEncryptor.PredictAESLength(length);
                CommunicationResult res = TryRead(clientIpPort, length, true);


                // Receive data
                int itemCount = 0;
                if (res.Instruction == TTInstruction.Transfer_TransferInfo)
                {
                    // Receive files individually
                    itemCount = BitConverter.ToInt32(res.Data, 0);
                    totalBytes = BitConverter.ToInt64(res.Data, 4);

                    for (int i = 0; i < itemCount; i++)
                    {
                        ReceiveItem();
                    }
                }
                else
                    throw new FailedReceivingException("Client sent wrong instruction about transfer.");

                transferStopwatch.Stop();
                OnRecordableEvent($"Sucessfully received {itemCount} item/s ({Explorer.ExplorerControl.FormatFileSize(bytesReceived)}) in {TTNet.FormatTimeSpan(transferStopwatch.Elapsed)} at average { Explorer.ExplorerControl.FormatFileSize(bytesReceived * 1000 / transferStopwatch.ElapsedMilliseconds)}/s.", Console.ConsoleMessageType.Common);
            });
        }
        private void ReceiveItem()
        {
            CommunicationResult res;


            // Receive item info
            int length = 1+8+1024;
            if (clientEncryptor != null)
                length = DataEncryptor.PredictAESLength(length);
            res = TryRead(clientIpPort, length, true);

            if (!(res.Instruction == TTInstruction.Transfer_FileInfo || res.Instruction == TTInstruction.Transfer_FolderInfo))
                throw new FailedReceivingException("Client sent unsupported item info.");


            // Receive item
            long size = BitConverter.ToInt64(res.Data, 0);
            string fullName = Encoding.UTF8.GetString(res.Data, 8, 512 - 9).Replace("\0", string.Empty);
            if (res.Instruction == TTInstruction.Transfer_FileInfo)
            {
                ReceiveFile(fullName, size);
            }
            else
            {
                ReceiveFolder(fullName);
            };
        }
        private void ReceiveFile(string fullName, long size)
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
            using (FileStream fs = File.Create($"{saveLocation}\\{fullName}"))
            {
                ReadResult result;
                int bufferSize = 0;
                while (bytesToReceive > 0)
                {
                    try
                    {
                        bufferSize = (int)Math.Min(bytesToReceive, maxBufferSize);
                        byte[] receivedBuffer = null;
                        if (useEncryption)
                        {
                            result = server.Read(clientIpPort, DataEncryptor.PredictAESLength(bufferSize));
                            receivedBuffer = clientEncryptor.AESDecryptBytes(result.Data);
                        }
                        else
                        {
                            result = server.Read(clientIpPort, bufferSize);
                            receivedBuffer = result.Data;
                        }


                        if (result.Status != ReadResultStatus.Success)
                        {
                            fs.Flush();
                            throw new Exception("Did not receive data in time.");
                        }


                        fs.Write(receivedBuffer, 0, bufferSize);
                    }
                    catch (Exception e)
                    {
                        fs.Flush();
                        throw new FailedReceivingException($"Failed while receiving '{fileName}' ({e.Message}).");
                    }


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
        private void ReceiveFolder(string fullName)
        {
            try
            {
                Directory.CreateDirectory($"{saveLocation}\\{fullName}");
            }
            catch(Exception)
            {
                throw new FailedReceivingException();
            }
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

        private void Events_TimerPermissionAskTimeout(object sender, ElapsedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (confirmationDialog != null)
                    confirmationDialog.Close();
            });

            timer.Stop();
            timer.Dispose();
        }
    }
}

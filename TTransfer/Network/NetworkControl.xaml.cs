using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;
using System.ComponentModel;

namespace TTransfer.Network
{
    


    public partial class NetworkControl : UserControl
    {
        public Action<string, Console.ConsoleMessageType> OnRecordableEvent;
        public Progress<TransferProgressReport> TransferProgress;
        public Func<List<Explorer.DirectoryItem>> GetSelectedItems;


        public bool IsBusy
        {
            get { return transferServer.IsBusy || transferClient.IsBusy; }
        }

        // Device properties
        private PhysicalAddressSerializable MacAddress;
        private IPAddress IPAddress;
        private IPAddress IPMask;
        private DeviceType DeviceType = DeviceType.Computer;

        // Interfaces
        private Socket socketUDP = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        private UdpClient clientUDP = new UdpClient(Settings.SettingsData.NetworkPresencePort);

        // Presence 
        private IPEndPoint PresenceEndPoint;
        private byte[] PresenceBuffer;
        
        // Timers
        private Timer PresenceTimer;
        private Timer PresenceTimeoutCheckTimer;

        // Threads
        private Thread presenceReceiveThread;

        // TCP
        private TransferServer transferServer;
        private TransferClient transferClient;
        

        


        public NetworkControl()
        {
            InitializeComponent();

            if (Properties.Settings.Default.Devices == null)
            {
                Properties.Settings.Default.Devices = new ObservableCollection<Device>() { };
                Properties.Settings.Default.Save();
            }

            DeviceListView.ItemsSource = Properties.Settings.Default.Devices;
            DeviceListView.DataContext = Properties.Settings.Default.Devices;

            // Sorting
            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(DeviceListView.ItemsSource);
            view.SortDescriptions.Add(new SortDescription("Online", ListSortDirection.Descending));
            view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Descending));

            TransferProgress = new Progress<TransferProgressReport>();
        }

        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            SetSendButtonEnabled(true);

            InitializeNetworkInfo();

            // TCP
            transferServer = new TransferServer(IPAddress, Settings.SettingsData.NetworkTransferPort, Settings.SettingsData.MaxNetworkPingMs, Settings.SettingsData.MaxBufferSize, TransferProgress);
            transferServer.OnRecordableEvent += OnRecordableEvent;
            transferServer.Start();
            transferClient = new TransferClient(Settings.SettingsData.NetworkTransferPort, Settings.SettingsData.MaxNetworkPingMs, Settings.SettingsData.MaxBufferSize, TransferProgress);
            transferClient.OnRecordableEvent += OnRecordableEvent;

            TTNet.GeneratePresenceBuffer(MacAddress, out PresenceBuffer, TTInstruction.Discovery_Present, Settings.SettingsData.Name, DeviceType);
            PresenceTimer = new Timer(new TimerCallback(PresenceSend), null, 2000, Settings.SettingsData.PresenceSendPeriod);
            PresenceTimeoutCheckTimer = new Timer(new TimerCallback(PresenceTimeoutTick), null, Settings.SettingsData.MaxNetworkPingMs, Settings.SettingsData.PresenceTimeoutCheckPeriod);
            presenceReceiveThread = new Thread(new ThreadStart(PresenceReceiveLoop));
            presenceReceiveThread.IsBackground = true;
            presenceReceiveThread.Start();
            PresenceHelloSend();
        }



        // UI
        private void SetSendButtonEnabled(bool enabled)
        {
            SendButton.Style = this.Resources[enabled ? "SendButton" : "SendButtonDisabled"] as Style;
            SendButton.IsHitTestVisible = enabled;
        }
        
        // General
        private void InitializeNetworkInfo()
        {
            IPAddress internetIp;
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;
                internetIp = endPoint.Address;
            }


            // Mac - this will either select the interface based on the selected mac address or the interface which will connect to internet
            MacAddress = null;

            PhysicalAddress backupMac = null;
            IPAddress backupIp = null;
            IPAddress backupMask = null;

            var nics = TTNet.GetUsableNetworkInterfaces();
            foreach (NetworkInterface nic in nics)
            {
                var interNetwork = nic.GetIPProperties().UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                IPAddress interfaceIp = interNetwork.Select(u => u.Address).FirstOrDefault();
                IPAddress interfaceMask = interNetwork.Select(u => u.IPv4Mask).FirstOrDefault();
                if (interfaceIp == null)
                    continue;
                PhysicalAddress interfaceMac = nic.GetPhysicalAddress();

                if (Settings.SettingsData.InterfaceMac != null && Settings.SettingsData.InterfaceMac.Equals(interfaceMac))
                {
                    MacAddress = interfaceMac;
                    IPAddress = interfaceIp;
                    IPMask = interfaceMask;
                    break;
                }

                if (interfaceIp.ToString() == internetIp.ToString())
                {
                    backupMac = interfaceMac;
                    backupIp = interfaceIp;
                    backupMask = interfaceMask;
                }
            }

            // If interface with selected mac doesn't exist, select the one which connect to internet
            if (MacAddress == null)
            {
                if(backupMac != null)
                {
                    MacAddress = backupMac;
                    IPAddress = internetIp;
                    IPMask = backupMask;
                    Settings.SettingsData.InterfaceMac = MacAddress;
                }
                else
                {
                    throw new Exception("Could not find a usable network interface.");
                }
            }

            // Broadcast endpoint
            PresenceEndPoint = new IPEndPoint(TTNet.GetBroadcastAddress(IPAddress, IPMask), Settings.SettingsData.NetworkPresencePort);
        }

        // Presence
        private void PresenceSend(object status)
        {
            socketUDP.SendTo(PresenceBuffer, PresenceEndPoint);
        }
        private void PresenceHelloSend()
        {
            byte[] buffer;
            TTNet.GeneratePresenceBuffer(MacAddress, out buffer, TTInstruction.Discovery_Hello, Settings.SettingsData.Name, DeviceType);

            socketUDP.SendTo(buffer, PresenceEndPoint);
        }
        private void PresenceByeSend()
        {
            byte[] buffer;
            TTNet.GeneratePresenceBuffer(MacAddress, out buffer, TTInstruction.Discovery_Bye);

            socketUDP.SendTo(buffer, PresenceEndPoint);
        }
        private void PresenceTimeoutTick(object status)
        {
            OnlineDevices.TimeoutDevices();
        }
        private void PresenceReceiveLoop()
        {
            IPEndPoint remoteIpEndPoint;
            PhysicalAddressSerializable remoteMac;
            TTInstruction instruction;
            List<byte> data;


            while (true)
            {
                remoteIpEndPoint = null;
                remoteMac = null;
                instruction = TTInstruction.Empty;
                data = null;

                if (TTNet.UnpackBuffer(clientUDP.Receive(ref remoteIpEndPoint), ref remoteMac, ref instruction, ref data))
                {
                    //OnRecordableEvent($"Received presence from: {remoteIpEndPoint.Address.ToString()}", Console.ConsoleMessageType.Common);

                    // Skip if from self
                    if (remoteIpEndPoint.Address.ToString() == IPAddress.ToString())
                        continue;

                    // Add or update device in saved list
                    if (instruction == TTInstruction.Discovery_Hello || instruction == TTInstruction.Discovery_Present)
                    {
                        if (!Enum.IsDefined(typeof(DeviceType), (int)data[0]))
                            continue;

                        DeviceType deviceType = (DeviceType)data[0];
                        string deviceName = Encoding.UTF8.GetString(data.GetRange(1, data.Count - 1).ToArray());

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Settings.SettingsData.AddUpdateDevice(remoteMac, deviceName, remoteIpEndPoint.Address, deviceType);
                        });
                    }

                    // Keep track of online status
                    switch (instruction)
                    {
                        case TTInstruction.Discovery_Hello:
                            OnlineDevices.SetOnline(remoteMac);
                            PresenceSend(null);
                            break;

                        case TTInstruction.Discovery_Present:
                            OnlineDevices.SetOnline(remoteMac);
                            break;

                        case TTInstruction.Discovery_Bye:
                            OnlineDevices.SetOffline(remoteMac);
                            break;
                    }
                }
            }
        }


        // Events
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            transferClient.StartTransfer(GetSelectedItems(), (Device)DeviceListView.SelectedItem);
        }
        private void DeviceListView_ReceiveModeButton_Click(object sender, RoutedEventArgs e)
        {
            PhysicalAddressSerializable clickMac = PhysicalAddressSerializable.Parse(((Button)sender).Tag.ToString());
            Settings.SettingsData.CycleDeviceReceiveMode(clickMac);
        }
        private void DeviceListView_Remove_Click(object sender, RoutedEventArgs e)
        {
            PhysicalAddressSerializable clickMac = PhysicalAddressSerializable.Parse(((Button)sender).Tag.ToString());
            Settings.SettingsData.RemoveDevice(clickMac);
        }
        private void DeviceListView_EncryptionButton_Click(object sender, RoutedEventArgs e)
        {
            PhysicalAddressSerializable clickMac = PhysicalAddressSerializable.Parse(((Button)sender).Tag.ToString());
            Device device = Settings.SettingsData.GetDevice(clickMac);

            if (device.EncryptionEnabled)
            {
                Settings.ConfirmationDialog dialog = new Settings.ConfirmationDialog("Remove encryption?", $"Do you want to remove encryption for communication with '{device.Name}'?", "Yes", "Cancel");
                dialog.Owner = Application.Current.MainWindow;
                if(dialog.ShowDialog() ?? false)
                {
                    Settings.SettingsData.SetDeviceEncryptionPassword(clickMac, null);
                }
            }
            else
            {
                EncryptionPasswordDialog dialog = new EncryptionPasswordDialog(device.Name);
                dialog.Owner = Application.Current.MainWindow;

                if(dialog.ShowDialog() ?? false)
                {
                    Settings.SettingsData.SetDeviceEncryptionPassword(clickMac, dialog.Password);
                }
            }
        }
        public void OnExit(object sender, ExitEventArgs e)
        {
            PresenceByeSend();
            PresenceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            PresenceTimer.Dispose();
            PresenceTimeoutCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);
            PresenceTimeoutCheckTimer.Dispose();

            transferServer.Stop();
            transferClient.TerminateConnection();
        }
    }
}

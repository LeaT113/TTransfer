using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Media;
using System.Windows;
using System.Windows.Interop;
using System.Net;
using System.Net.NetworkInformation;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Security;

namespace TTransfer.Network
{
    public enum DeviceReceiveMode
    {
        Deny,
        AskEachTime,
        AllowAlways
    }
    public enum DeviceType
    {
        Unknown,
        Computer,
        Phone
    }


    [Serializable]
    public class Device : INotifyPropertyChanged
    {
        private static readonly ImageSource[] DeviceTypeIcons = new ImageSource[] { null, new BitmapImage(new Uri("Icons/DeviceType_Computer.png", UriKind.Relative)), new BitmapImage(new Uri("Icons/DeviceType_Phone.png", UriKind.Relative)) };
        private static readonly ImageSource[] ReceiveModeIcons = new ImageSource[] { new BitmapImage(new Uri("Icons/ReceiveMode_Deny.png", UriKind.Relative)), new BitmapImage(new Uri("Icons/ReceiveMode_AskEachTime.png", UriKind.Relative)), new BitmapImage(new Uri("Icons/ReceiveMode_AllowAlways.png", UriKind.Relative)) };
        private static readonly ImageSource[] EncryptionModeIcons = new ImageSource[] { new BitmapImage(new Uri("Icons/Encryption_Off.png", UriKind.Relative)), new BitmapImage(new Uri("Icons/Encryption_On.png", UriKind.Relative)) };
        private static readonly string[] ReceiveModeTooltips = new string[] { "Don't receive files from this device.", "Ask for permission when device wants to send files.", "Always allow this device to send files." };
        private static readonly string[] EncryptionModeTooltips = new string[] { "Encryption disabled. Click to enable encryption with password.", "Encryption enabled. Click to remove encryption" };
        public static Action<string, Console.ConsoleMessageType> OnRecordableEvent;


        // Visible
        public ImageSource DeviceIcon { get { return DeviceTypeIcons[(int)type]; } }
        public string Name { get { return name; } }
        public ImageSource ReceiveModeIcon { get { return ReceiveModeIcons[(int)receiveMode]; } }
        public string ReceiveModeTooltip { get { return ReceiveModeTooltips[(int)receiveMode]; } }
        public string MacAddressString { get { return mac.ToString(); } }
        public ImageSource EncryptionModeIcon { get { if (EncryptionEnabled) return EncryptionModeIcons[1]; else return EncryptionModeIcons[0]; } }
        public string EncryptionModeTooltip { get { if (EncryptionEnabled) return EncryptionModeTooltips[1]; else return EncryptionModeTooltips[0]; } }
       
        // Other
        public bool Online { get { return OnlineDevices.IsOnline(mac); } }
        public bool EncryptionEnabled { get { return !(encryptionPassword == "" || encryptionPassword == null); } }
        public DeviceType Type { get { return type; } }
        public DeviceReceiveMode ReceiveMode { get { return receiveMode; } }
        public IPAddress IPAddress { get { return ip; } }
        public PhysicalAddressSerializable MacAdress { get { return mac; } }
        public SecureString EncryptionPassword { get { var sec = new SecureString(); foreach (char c in encryptionPassword) sec.AppendChar(c); return sec; } }

        // Styles
        public string RemoveButtonStyle { get { return Online ? "InvisibleElement" : "WindowsButton"; } }
        public string ListViewItemStyle { get { return Online ? "ListViewItemOnline" : "ListViewItemOffline"; } }


        // Internal
        string name;
        DeviceType type;
        DeviceReceiveMode receiveMode;
        IPAddress ip;
        PhysicalAddressSerializable mac;
        string encryptionPassword;

        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;
        


        public Device()
        {
            name = null;
            ip = null;
            mac = null;
            type = DeviceType.Unknown;
            receiveMode = DeviceReceiveMode.Deny;
        }
        public Device(string name, IPAddress ipAddress, PhysicalAddressSerializable macAddress, DeviceType type)
        {
            this.name = name;
            this.ip = ipAddress;
            this.mac = macAddress;
            this.type= type;
            this.receiveMode = DeviceReceiveMode.Deny;
        }



        public void NotifyPropertyChanged()
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(""));
            }
        }


        /// <summary>
        /// Sets name and IP to values and returns true if they were changed.
        /// </summary>
        public bool TryUpdateInfo(string newName, IPAddress newIP)
        {
            bool changed = false;

            if (newName != null && this.name != newName)
            {
                OnRecordableEvent($"{this.name} changed name to '{newName}'", Console.ConsoleMessageType.Common);

                this.name = newName;
                changed = true;
            }

            if (newIP != null && this.ip.ToString() != newIP.ToString())
            {
                OnRecordableEvent($"{this.name}'s IP has changed from {this.ip.ToString()} to {newIP.ToString()}", Console.ConsoleMessageType.Warning);

                this.ip = newIP;
                changed = true;
            }

            if (changed)
                NotifyPropertyChanged();

            return changed;
        }
        
        /// <summary>
        /// Changed receive mode to next option
        /// </summary>
        public void CycleReceiveMode()
        {
            int newRMindex = (int)receiveMode + 1;
            if (newRMindex > 2)
                newRMindex = 0;
            receiveMode = (DeviceReceiveMode)newRMindex;

            NotifyPropertyChanged();
        }


        public void SetEncryptionPassword(SecureString password)
        {
            encryptionPassword = new NetworkCredential("", password).Password;

            NotifyPropertyChanged();
        }
        public void RemoveEncryptionPassword()
        {
            encryptionPassword = null;

            NotifyPropertyChanged();
        }

        // Operators
        public override bool Equals(object comparand)
        {
            Device device = comparand as Device;
            if (device == null)
                return false;

            return this.mac == device.mac;
        }
    }
}

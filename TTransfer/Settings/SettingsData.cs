using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace TTransfer.Settings
{
    public static class SettingsData
    {
        // Network
        public const int MaxNetworkPingMs = 5000;

        // Network discovery settings
        public const int NetworkPresencePort = 11500;
        public const int NetworkTransferPort = 11501;
        public const int PresenceSendPeriod = 10000;
        public const int PresenceTimeoutCheckPeriod = 3000;

        // Network transfer settings
        public const int MaxBufferSize = 1024;
        public const int MaxPermissionAskWaitMs = 5000;
        public const int MaxNameLength = 20;


        public static Action SettingsChanged { get; set; }


        /// <summary>
        /// User-set network name.
        /// </summary>
        public static string Name
        {
            get
            {
                if (Properties.Settings.Default.Name.Length > MaxNameLength)
                    Name = Name;
                return Properties.Settings.Default.Name;
            }
            set
            {
                if (value.Length > MaxNameLength)
                    Properties.Settings.Default.Name = value.Substring(0, MaxNameLength);
                else Properties.Settings.Default.Name = value;
                Properties.Settings.Default.Save();
                if (SettingsChanged != null) SettingsChanged();
            }
        }

        /// <summary>
        /// Location, in which temporary and downloaded files will be stores.
        /// </summary>
        public static string SaveLocation
        {
            get { return Properties.Settings.Default.DownloadURI; }
            set { Properties.Settings.Default.DownloadURI = value; Properties.Settings.Default.Save(); if (SettingsChanged != null) SettingsChanged(); ; }
        }

        /// <summary>
        /// Maximum amount of time that the application will wait for a network response in miliseconds.
        /// </summary>

        /// <summary>
        /// Mac address of the selected network interface
        /// </summary>
        public static Network.PhysicalAddressSerializable InterfaceMac
        {
            get { return Properties.Settings.Default.InterfaceMac; }
            set { Properties.Settings.Default.InterfaceMac = value; Properties.Settings.Default.Save(); if(SettingsChanged != null) SettingsChanged(); }
        }

        /// <summary>
        /// Does the user want to see hidden files or not
        /// </summary>
        public static bool ShowHiddenFiles
        {
            get { return Properties.Settings.Default.ShowHiddenFiles; }
            set 
            {
                Properties.Settings.Default.ShowHiddenFiles = value;
                Properties.Settings.Default.Save();
                if (SettingsChanged != null) SettingsChanged();
            }
        }

        public static bool ShowNetworkDrives
        {
            get { return Properties.Settings.Default.ShowNetworkDrives; }
            set
            {
                Properties.Settings.Default.ShowNetworkDrives = value;
                Properties.Settings.Default.Save();
                if (SettingsChanged != null) SettingsChanged();
            }
        }



        // Devices
        /// <summary>
        /// Returns device with specified Mac from saved devices.
        /// </summary>
        public static Network.Device GetDevice(Network.PhysicalAddressSerializable mac)
        {
            if (mac == null)
                return null;

            foreach (var dev in Properties.Settings.Default.Devices)
            {
                if (dev == null || dev.MacAdress == null)
                    continue;

                if (dev.MacAdress.Equals(mac))
                    return dev;
            }

            return null;
        }
        /// <summary>
        /// Returns device with specified IP from saved devices.
        /// </summary>
        public static Network.Device GetDevice(IPAddress ip)
        {
            if (ip == null)
                return null;

            foreach(var dev in Properties.Settings.Default.Devices)
            {
                if (dev == null || dev.IPAddress == null)
                    continue;

                if (dev.IPAddress.ToString() == ip.ToString())
                    return dev;
            }

            return null;
        }
        /// <summary>
        /// Returns device using index in list
        /// </summary>
        public static Network.Device GetDevice(int index)
        {
            if (index < 0 || index >= Properties.Settings.Default.Devices.Count)
                return null;

            return Properties.Settings.Default.Devices[index];
        }
        /// <summary>
        /// Adds new device into list or updates info on already existing device.
        /// </summary>
        public static void AddUpdateDevice(Network.PhysicalAddressSerializable mac, string name, IPAddress ip, Network.DeviceType? type = null)
        {
            Network.Device device = GetDevice(mac);

            if (device != null)
            {
                if (device.TryUpdateInfo(name, ip))
                    Properties.Settings.Default.Save();
            }
            else
            {
                device = new Network.Device(name, ip, mac, type ?? Network.DeviceType.Unknown);
                Properties.Settings.Default.Devices.Add(device);
                Properties.Settings.Default.Save();
                device.NotifyPropertyChanged();
            }
        }
        public static int GetDeviceCount()
        {
            if (Properties.Settings.Default.Devices == null)
                return -1;
            else return Properties.Settings.Default.Devices.Count;
        }


        
        
        public static void CycleDeviceReceiveMode(Network.PhysicalAddressSerializable mac)
        {
            Network.Device device = GetDevice(mac);

            if(device != null)
            {
                device.CycleReceiveMode();
                Properties.Settings.Default.Save();
            }
        }

        /// <summary>
        /// Sets a password for encryption for device. If password is null, will remove password.
        /// </summary>
        /// <param name="mac"></param>
        /// <param name="password"></param>
        public static void SetDeviceEncryptionPassword(Network.PhysicalAddressSerializable mac, SecureString password)
        {
            Network.Device device = GetDevice(mac);

            if (device != null)
            {
                if (password == null)
                    device.RemoveEncryptionPassword();
                else
                    device.SetEncryptionPassword(password);

                Properties.Settings.Default.Save();
            }
        }

        /// <summary>
        /// Removes device with specified Mac from saved devices.
        /// </summary>
        public static void RemoveDevice(Network.PhysicalAddressSerializable mac)
        {
            Network.Device device = GetDevice(mac);

            if(device != null)
            {
                Properties.Settings.Default.Devices.Remove(device);
                Properties.Settings.Default.Save();
            }
        }
    }
}

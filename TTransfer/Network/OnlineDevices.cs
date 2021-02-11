using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TTransfer.Network
{
    public static class OnlineDevices
    {
        public static Action<string, Console.ConsoleMessageType> OnRecordableEvent;

        static List<PhysicalAddressSerializable> macAddresses = new List<PhysicalAddressSerializable>();
        static List<DateTime> lastPresenceTimes = new List<DateTime>();

        
        /// <summary>
        /// Returns true if device with this Mac is online.
        /// </summary>
        public static bool IsOnline(PhysicalAddressSerializable mac)
        {
            return macAddresses.Contains(mac);
        }

        /// <summary>
        /// Set this device to be online through next timeout.
        /// </summary>
        public static void SetOnline(PhysicalAddressSerializable mac)
        {
            if (!macAddresses.Contains(mac))
            {
                macAddresses.Add(mac);
                lastPresenceTimes.Add(DateTime.UtcNow);
                Settings.SettingsData.GetDevice(mac).NotifyPropertyChanged();
            }
            else
            {
                lastPresenceTimes[macAddresses.IndexOf(mac)] = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Set this device to be set offline during next timeout.
        /// </summary>
        public static void SetOffline(PhysicalAddressSerializable mac)
        {
            lastPresenceTimes[macAddresses.IndexOf(mac)] = DateTime.UtcNow.Subtract(TimeSpan.FromDays(1));
        }

        /// <summary>
        /// Go though all online devices and remove any, which haven't sent a presence packet in the designated time interval.
        /// </summary>
        public static void TimeoutDevices()
        {
            DateTime currentTimeUTC = DateTime.UtcNow;

            for (int i = 0; i < macAddresses.Count; i++)
            {
                PhysicalAddressSerializable removedMac = macAddresses[i];
                TimeSpan difference = currentTimeUTC.Subtract(lastPresenceTimes[i]);
                if (difference.TotalMilliseconds > Settings.SettingsData.PresenceSendPeriod + Settings.SettingsData.MaxNetworkPingMs)
                {
                    macAddresses.RemoveAt(i);
                    lastPresenceTimes.RemoveAt(i);
                    i--;
                }
                Settings.SettingsData.GetDevice(removedMac).NotifyPropertyChanged();
            }
        }
    }
}

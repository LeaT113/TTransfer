using Syroot.Windows.IO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TTransfer.Explorer;

namespace TTransfer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();


            // Set default settings
            if (Settings.SettingsData.Name == "")
                Settings.SettingsData.Name = Environment.MachineName;

            if (Settings.SettingsData.SaveLocation == "")
                Settings.SettingsData.SaveLocation = new KnownFolder(KnownFolderType.Downloads).Path;
        }



        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            Explorer.OnRecordableEvent += Console.AddMessage;
            NetworkControl.OnRecordableEvent += Console.AddMessage;
            Network.OnlineDevices.OnRecordableEvent += Console.AddMessage;
            Network.Device.OnRecordableEvent += Console.AddMessage;

            NetworkControl.GetSelectedItems = Explorer.GetSelectedItems;



            Application.Current.Exit += NetworkControl.OnExit;
            Application.Current.Exit += Console.OnExit;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.Windows.OfType<Settings.SettingsMenu>().Count() == 0)
            {
                var win = new Settings.SettingsMenu();
                win.Owner = Application.Current.MainWindow;
                win.DirectorySettingsChanged += Explorer.ReloadPath;
                win.Show();
            }
        }

        private void DownloadLocationButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo(Settings.SettingsData.SaveLocation));
        }

        protected override void OnClosing(CancelEventArgs e)
        {

        }
    }
}

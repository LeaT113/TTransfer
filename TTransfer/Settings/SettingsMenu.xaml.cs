using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TextBox = System.Windows.Controls.TextBox;

namespace TTransfer.Settings
{
    /// <summary>
    /// Interaction logic for SettingsMenu.xaml
    /// </summary>
    public partial class SettingsMenu : Window, INotifyPropertyChanged
    {
        // Public
        public event PropertyChangedEventHandler PropertyChanged;
        public string DeviceName { get { return SettingsData.Name; } }
        public string DownloadLocation { get { return SettingsData.SaveLocation; } }
        public ObservableCollection<string> NetworkInterfaceNames { get { return networkInterfaceNames; } }
        public string NetIP 
        { 
            get 
            { 
                return networkInterfaces[NetworkInterfaceComboBox.SelectedIndex].GetIPProperties().UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(u => u.Address)
                    .FirstOrDefault()
                    .ToString(); 
            } 
        }

        public bool SlowSending
        {
            get { return SettingsData.SlowSending; }
            set
            {
                SettingsData.SlowSending = value;
                OnSettingsChanged();
            }
        }
        public bool ShowHiddenFiles
        {
            get { return SettingsData.ShowHiddenFiles; }
            set
            {
                SettingsData.ShowHiddenFiles = value;
                OnSettingsChanged();
            }
        }
        public bool ShowNetworkDrives
        {
            get { return SettingsData.ShowNetworkDrives; }
            set
            {
                SettingsData.ShowNetworkDrives = value;
                OnSettingsChanged();
            }
        }

        public Action DirectorySettingsChanged;
        public Action NetworkSettingsChanged;

        // Internal
        NetworkInterface[] networkInterfaces;
        ObservableCollection<string> networkInterfaceNames;
        bool ChangingName { get { return NameTextBox.IsHitTestVisible; } }
        bool nameValid = false;



        public SettingsMenu()
        {
            InitializeComponent();
            DataContext = this;

            NameTextBox.MaxLength = SettingsData.MaxNameLength;

            networkInterfaces = Network.TTNet.GetUsableNetworkInterfaces();
            networkInterfaceNames = new ObservableCollection<string>(networkInterfaces.Select(u => u.Name));
            NetworkInterfaceComboBox.SelectedIndex = networkInterfaces
                .Select((intr, index) => new { intr, index })
                .Where(x => SettingsData.InterfaceMac.Equals(x.intr.GetPhysicalAddress()))
                .Select(x => x.index)
                .First();

            SettingsData.SettingsChanged += OnSettingsChanged;
            OnSettingsChanged();
        }


        // Events
        private void OnSettingsChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        }

        private void SelectLocationButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = SettingsData.SaveLocation;
                dialog.Description = "Select a location to save received data.";
                dialog.ShowNewFolderButton = true;
                DialogResult result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    SettingsData.SaveLocation = dialog.SelectedPath;
                }
            }
        }

        private void NameChangeButton_Click(object sender, RoutedEventArgs e)
        {
            if(!ChangingName)
            {
                NameTextBox.Text = SettingsData.Name;
                NameTextBox.Visibility = Visibility.Visible;
                NameTextBox.IsHitTestVisible = true;
                NameTextBox.SelectAll();
                NameTextBox.Focus();
                NameTextBox.CaretIndex = NameTextBox.Text.Length;
            } 
            else
            {
                if(nameValid)
                {
                    SettingsData.Name = NameTextBox.Text;
                }


                NameTextBox.Visibility = Visibility.Hidden;
                NameTextBox.IsHitTestVisible = false;
                NameTextBox.Text = "";
                NameChangeButton.Content = "Change name";
            }
        }

        private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if(NameTextBox.Text.Length > 3)
            {
                NameChangeButton.Content = "Save";
                NameChangeButton.ToolTip = null;
                nameValid = true;
            }
            else
            {
                NameChangeButton.Content = "Cancel";
                NameChangeButton.ToolTip = "Name must be at least 4 characters long.";
                //TODO Red border when too short.
                nameValid = false;
            }
        }

        private void NetworkInterfaceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SettingsData.InterfaceMac = networkInterfaces[NetworkInterfaceComboBox.SelectedIndex].GetPhysicalAddress();
        }

        private void DirectoryCheckbox_Click(object sender, RoutedEventArgs e)
        {
            if (DirectorySettingsChanged != null)
                DirectorySettingsChanged();
        }

        private void SlowSendingCheckbox_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
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
using TTransfer.Console;

namespace TTransfer.Explorer
{
    public enum DirectoryMove
    {
        Unique,
        Back,
        Forward,
        ExcludeHistory
    }
    public enum SortType
    {
        Name,
        LastModified,
        Size,
        Extension,
    }

    public partial class ExplorerControl : UserControl, INotifyPropertyChanged
    {
        // Static
        static readonly BitmapImage arrowLight = new BitmapImage(new Uri("Icons/arrow_light.ico", UriKind.Relative));
        static readonly BitmapImage arrowDark = new BitmapImage(new Uri("Icons/arrow_dark.ico", UriKind.Relative));
        static readonly ObservableCollection<string> sortOptions = new ObservableCollection<string>() { "Name", "Last modified", "Size", "Extension" };
        static readonly string[] fileSizes = { "B", "KB", "MB", "GB", "TB", "PB" };

        // Public
        public ObservableCollection<DriveItem> DriveItems { get { return driveItems; } }
        public ObservableCollection<DirectoryItem> DirectoryItems { get { return directoryItems; } }
        public ObservableCollection<string> UriParts 
        { 
            get 
            {
                if (activePath == null || activePath == "")
                    return null;

                return new ObservableCollection<string>(activePath.Split('\\'));
            } 
        }
        public ObservableCollection<string> SortOptions { get { return sortOptions; } }
        
        // TODO Remove
        public bool IsFolderSelected { 
            get
            {
                foreach (var item in GetSelectedItems())
                    if (item.IsFolder)
                        return true;
                return false;
            } 
        }

        // Data
        ObservableCollection<DriveItem> driveItems;
        ObservableCollection<DirectoryItem> directoryItems;
        string activePath;
        Stack<string> pathHistory;
        Stack<string> pathForwardHistory;
        bool keyboardNavigation = true;

        // Event
        public Action<string, ConsoleMessageType> OnRecordableEvent;
        public event PropertyChangedEventHandler PropertyChanged;



        public ExplorerControl()
        {
            InitializeComponent();
            DataContext = this;

            driveItems = new ObservableCollection<DriveItem>();
            directoryItems = new ObservableCollection<DirectoryItem>();

            pathHistory = new Stack<string>();
            pathForwardHistory = new Stack<string>();
        }



        // Events
        private void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            ListPath("", DirectoryMove.ExcludeHistory);
        }

        protected void DriveListView_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var track = ((ListViewItem)sender).Content as Track; //Casting back to the binded Track

            Open(((ListViewItem)sender).DataContext as DriveItem);
        }
        private void DriveListView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DriveListView.UnselectAll();
        }

        protected void DirectoryListView_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var track = ((ListViewItem)sender).Content as Track; //Casting back to the binded Track

            // Double click interaction
            Open(((ListViewItem)sender).DataContext as DirectoryItem);
        }
        private void DirectoryListView_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DirectoryListView.UnselectAll();
        }

        private void BackDirButton_Click(object sender, RoutedEventArgs e)
        {
            GoBack();
        }
        private void ForwardDirButton_Click(object sender, RoutedEventArgs e)
        {
            GoForward();
        }
        private void UpDirButton_Click(object sender, RoutedEventArgs e)
        {
            GoUp();
        }
        private void UserControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            switch (e.ChangedButton)
            {
                //Back button
                case MouseButton.XButton1:
                    GoBack();
                    break;

                //forward button
                case MouseButton.XButton2:
                    GoForward();
                    break;

                default:
                    break;
            }
        }
        private void UriPartsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UriPartsListView.SelectedIndex == -1)
                return;

            int index = UriPartsListView.SelectedIndex;

            string[] path = activePath.Split('\\');
            string uri = "";
            for (int i = 0; i <= index; i++)
                uri += path[i] + (i == index ? "" : "\\");
            ListPath(uri, DirectoryMove.Unique);

            UriPartsListView.SelectedItem = null;
        }

        private void UriText_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Disable navigation
            keyboardNavigation = false;

            UriText.Text = activePath;
            UriPartsListView.Visibility = Visibility.Hidden;
            UriPartsListView.IsHitTestVisible = false;
            UriText.KeyDown += UriText_KeyDown;

            _ = UriTextSelectAll();
        }
        private async Task UriTextSelectAll()
        {
            await Task.Run(() =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UriText.Focus();
                    UriText.SelectAll();
                });
            });
        }
        private void UriText_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Enable navigation
            keyboardNavigation = true;

            UriText.Text = "";
            UriText.KeyDown -= UriText_KeyDown;
            UriPartsListView.Visibility = Visibility.Visible;
            UriPartsListView.IsHitTestVisible = true;
        }
        private void UriText_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    if (ListPath(UriText.Text, DirectoryMove.Unique))
                        Keyboard.ClearFocus();
                    else
                    {
                        UriText.LostKeyboardFocus -= UriText_LostKeyboardFocus;
                        UriText.GotKeyboardFocus -= UriText_GotKeyboardFocus;
                        MessageBox.Show("The directory '" + UriText.Text + "' does not exist.", "TTransfer", MessageBoxButton.OK, MessageBoxImage.Error);
                        UriText.LostKeyboardFocus += UriText_LostKeyboardFocus;
                        UriText.GotKeyboardFocus += UriText_GotKeyboardFocus;
                    }
                    break;

                case Key.Escape:
                    Keyboard.ClearFocus();
                    break;
            }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e)
        {
            ReloadPath();
        }

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            // Ignore if typing
            if (!keyboardNavigation)
                return;

            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (Keyboard.IsKeyDown(Key.Left))
                    GoBack();
                else if (Keyboard.IsKeyDown(Key.Right))
                    GoForward();
            }
            else
                switch (e.Key)
                {
                    case Key.Enter:
                        if (activePath == "")
                            Open(DriveListView.SelectedItem as DriveItem);
                        else
                            Open(DirectoryListView.SelectedItem as DirectoryItem);
                        break;
                    case Key.Back:
                        GoUp();
                        break;
                }
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SortBy((SortType)SortComboBox.SelectedIndex);
        }

        private void OnPropertyChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        }


        // Navigation
        public bool ListPath(string path, DirectoryMove directoryMove)
        {
            path = FormatURI(path);

            if (!Directory.Exists(path) && path != "")
                return false;  

            if (path != "" && !CanRead(path))
                return false;


            // History
            switch (directoryMove)
            {
                case DirectoryMove.Unique:
                    pathHistory.Push(activePath);
                    pathForwardHistory.Clear();
                    break;

                case DirectoryMove.Back:
                    pathForwardHistory.Push(activePath);
                    break;

                case DirectoryMove.Forward:
                    pathHistory.Push(activePath);
                    break;
            }


            // Load drives or directory
            if (path == "")
            {
                DriveListView.Visibility = Visibility.Visible;
                DriveListView.IsHitTestVisible = true;
                DirectoryListView.Visibility = Visibility.Hidden;
                DirectoryListView.IsHitTestVisible = false;
                SortComboBox.Visibility = Visibility.Hidden;
                SortComboBox.IsHitTestVisible = false;

                // Load drives
                // TODO Async loading to not lag app?
                driveItems = new ObservableCollection<DriveItem>(
                    DriveInfo.GetDrives()
                    .Where(x => x.DriveType == DriveType.Fixed || x.DriveType == DriveType.Removable || (Settings.SettingsData.ShowNetworkDrives && x.DriveType == DriveType.Network && x.IsReady == true))
                    .Select(x => new DriveItem(x))
                    .ToList()
                    );

                // Scroll to top
                if (DriveListView.Items.Count > 0)
                    DriveListView.ScrollIntoView(DriveListView.Items[0]);

                Keyboard.Focus(DriveListView);
            }
            else
            {
                DriveListView.Visibility = Visibility.Hidden;
                DriveListView.IsHitTestVisible = false;
                DirectoryListView.Visibility = Visibility.Visible;
                DirectoryListView.IsHitTestVisible = true;
                SortComboBox.Visibility = Visibility.Visible;
                SortComboBox.IsHitTestVisible = true;

                
                directoryItems = new ObservableCollection<DirectoryItem>(
                    Directory.GetDirectories(path + '\\')
                    .Select(u => new DirectoryInfo(u))
                    .Where(i => !i.Attributes.HasFlag(FileAttributes.System) && (Settings.SettingsData.ShowHiddenFiles) || !i.Attributes.HasFlag(FileAttributes.Hidden)) 
                    .Select(u => new DirectoryItem(u)).ToList()
                    .Concat(
                    Directory.GetFiles(path + '\\')
                    .Select(u => new FileInfo(u))
                    .Where(i => !i.Attributes.HasFlag(FileAttributes.System) && (Settings.SettingsData.ShowHiddenFiles) || !i.Attributes.HasFlag(FileAttributes.Hidden))
                    .Select(u => new DirectoryItem(u)).ToList()
                    ));

                // Scroll to top when changing directories
                if (DirectoryListView.Items.Count > 0)
                    DirectoryListView.ScrollIntoView(DirectoryListView.Items[0]);

                Keyboard.Focus(DirectoryListView);


                _ = UpdateIcons();
            }
            

            // Update buttons
            (BackDirButton.Content as Image).Source = pathHistory.Count() > 0 ? arrowLight : arrowDark;
            (ForwardDirButton.Content as Image).Source = pathForwardHistory.Count() > 0 ? arrowLight : arrowDark;

            SortComboBox.SelectedIndex = 0;
            activePath = path;
            OnPropertyChanged();

            return true;
        }
        public void Open(DriveItem item)
        {
            ListPath(item.Path, DirectoryMove.Unique);
        }
        public void Open(DirectoryItem item)
        {
            if (item.IsFolder)
                ListPath(item.Path, DirectoryMove.Unique);
            else
                Process.Start(item.Path);
        }
        public void GoBack()
        {
            if (pathHistory.Count() > 0)
                ListPath(pathHistory.Pop(), DirectoryMove.Back);
        }
        public void GoForward()
        {
            if(pathForwardHistory.Count() > 0)
                ListPath(pathForwardHistory.Pop(), DirectoryMove.Forward);
        }
        public void GoUp()
        {
            string[] path = activePath.Split('\\');
            if (path.Count() > 1)
                ListPath(activePath.Substring(0, activePath.Length - path.Last().Length - 1), DirectoryMove.Unique);
            else
                ListPath("", DirectoryMove.Unique);
        }
        public void SortBy(SortType sortBy)
        {
            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(DirectoryListView.ItemsSource);
            
            view.SortDescriptions.Clear();
            switch(sortBy)
            {
                // Name is default
                case SortType.LastModified:
                    view.SortDescriptions.Add(new SortDescription("LastModified", ListSortDirection.Descending));
                    break;
                
                case SortType.Size:
                    view.SortDescriptions.Add(new SortDescription("Size", ListSortDirection.Descending));
                    break;

                case SortType.Extension:
                    view.SortDescriptions.Add(new SortDescription("Extension", ListSortDirection.Ascending));
                    view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
                    break;
            }

            // Scroll to top
            if (DirectoryListView.Items.Count > 0)
                DirectoryListView.ScrollIntoView(DirectoryListView.Items[0]);
        }
        public void ReloadPath()
        {
            ListPath(activePath, DirectoryMove.ExcludeHistory);
        }
        public async Task UpdateIcons()
        {
            for (int i = 0; i < directoryItems.Count; i++)
            {
                directoryItems[i].SetIcon(await IconService.GetIconAsync(directoryItems[i].Extension, directoryItems[i].Path));
            }
        }


        // Other
        public List<DirectoryItem> GetSelectedItems()
        {
            return DirectoryListView.SelectedItems.Cast<DirectoryItem>().ToList();
        }
        private string FormatURI(string uri)
        {
            string newUri = uri;

            newUri =  newUri.Replace('/', '\\');
            
            if (newUri.Length > 0 && newUri.Last() == '\\')
                newUri = newUri.Remove(newUri.Length - 1, 1);

            return newUri;
        }
        public static bool CanRead(string path)
        {
            try
            {
                var readAllow = false;
                var readDeny = false;
                var accessControlList = Directory.GetAccessControl(path);
                if (accessControlList == null)
                    return false;

                //get the access rules that pertain to a valid SID/NTAccount.
                var accessRules = accessControlList.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
                if (accessRules == null)
                    return false;

                //we want to go over these rules to ensure a valid SID has access
                foreach (FileSystemAccessRule rule in accessRules)
                {
                    if ((FileSystemRights.Read & rule.FileSystemRights) != FileSystemRights.Read) continue;

                    if (rule.AccessControlType == AccessControlType.Allow)
                        readAllow = true;
                    else if (rule.AccessControlType == AccessControlType.Deny)
                        readDeny = true;
                }

                return readAllow && !readDeny;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }   
        public static string FormatFileSize(long bytes)
        {
            int order = 0;
            long sizeCalc = bytes;
            while (sizeCalc >= 1000 && order < fileSizes.Length - 1)
            {
                order++;
                sizeCalc = sizeCalc / 1000;
            }
            // Adjust the format string to your preferences. For example "{0:0.#}{1}" would show a single decimal place, and no space.
            return String.Format("{0:0.##} {1}", sizeCalc, fileSizes[order]);
        }
        
    }
}

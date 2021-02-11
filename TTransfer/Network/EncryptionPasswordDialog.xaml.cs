using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace TTransfer.Network
{
    /// <summary>
    /// Interaction logic for EncryptionPasswordDialog.xaml
    /// </summary>
    public partial class EncryptionPasswordDialog : Window
    {
        public SecureString Password { get { return PasswordTextBox.SecurePassword; } }

        bool okEnabled = false;


        public EncryptionPasswordDialog()
        {
            InitializeComponent();
        }
        public EncryptionPasswordDialog(string hostname)
        {
            InitializeComponent();
            QuestionLabel.Text = $"Choose a password for encrypting communication with '{hostname}'. The other user must use the same password. Minimum 8 characters.";
            PasswordTextBox.Focus();
        }

        private void DialogButtonOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }

        private void PasswordTextBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            SetEnableOk(PasswordTextBox.SecurePassword.Length >= 8);
        }

        
        private void SetEnableOk(bool enabled)
        {
            DialogButtonOk.Style = Application.Current.FindResource(enabled ? "DialogButton" : "DialogButtonDisabled") as Style;
            okEnabled = enabled;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !okEnabled)
                e.Handled = true;
        }
    }
}

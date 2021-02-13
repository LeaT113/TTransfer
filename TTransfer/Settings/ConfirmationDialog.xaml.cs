using System;
using System.Collections.Generic;
using System.Linq;
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

namespace TTransfer.Settings
{
    /// <summary>
    /// Interaction logic for YesNoDialog.xaml
    /// </summary>
    public partial class ConfirmationDialog : Window
    {
        public ConfirmationDialog(string title, string question, string okButtonText, string cancelButtonText)
        {
            InitializeComponent();
            Title = title;
            QuestionLabel.Text = question;
            DialogButtonOk.Content = okButtonText;
            DialogButtonCancel.Content = cancelButtonText;
        }

        private void DialogButtonOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}

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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TTransfer.Network
{
    /// <summary>
    /// Interaction logic for TransferProgressBar.xaml
    /// </summary>
    public partial class TransferProgressBar : UserControl
    {
        public TransferProgressBar()
        {
            InitializeComponent();
        }



        public void TransferProgressChanged(object sender, TransferProgressReport e)
        {
            if (e.PercentDone == 100)
            {
                MyGrid.Style = Application.Current.Resources["InvisibleElement"] as Style;
            }
            else
            {
                MyGrid.Style = null;
                ProgressBar.Value = e.PercentDone;
                string s = e.IsSender ? "Sending" : "Receiving";
                ActiveItemTextBlock.Text = $"{s} {e.ActiveItem}";
                SizeTextBlock.Text = e.TotalSize;
            }
        }
    }
}

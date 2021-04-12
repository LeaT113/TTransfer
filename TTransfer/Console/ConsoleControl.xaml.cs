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

namespace TTransfer.Console
{
    public enum ConsoleMessageType
    {
        Common,
        Warning,
        Error
    }



    public partial class ConsoleControl : UserControl
    {
        // Static
        static Brush warningColor = new SolidColorBrush(Color.FromRgb(232, 219, 70));
        static Brush errorColor = new SolidColorBrush(Color.FromRgb(217, 63, 52));

        // Internal
        bool enabled = true;



        public ConsoleControl()
        {
            InitializeComponent();
            AddMessage("TTransfer booted successfully.", ConsoleMessageType.Common);
        }

        

        public void AddMessage(string message, ConsoleMessageType type)
        {
            if (!enabled)
                return;


            string content = $"{GetClockString()} {message}\n";
            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (type)
                {
                    case ConsoleMessageType.Common:
                        OutputTextBlock.Inlines.Add(new Run(content));
                        break;

                    case ConsoleMessageType.Warning:
                        OutputTextBlock.Inlines.Add(new Run(content) { Foreground = warningColor});
                        break;

                    case ConsoleMessageType.Error:
                        OutputTextBlock.Inlines.Add(new Run(content) { Foreground = errorColor });
                        break;
                }

                ScrollViewer.ScrollToBottom();
            });
        }

        public void OnExit(object sender, ExitEventArgs e)
        {
            enabled = false;
        }

        private string GetClockString()
        {
            return "[" + DateTime.Now.ToString("HH:mm:ss") + "]";
        }
    }
}

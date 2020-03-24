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

namespace SecuCodeApp
{
    /// <summary>
    /// Interaction logic for BasicMessageBox.xaml
    /// </summary>
    public partial class BasicMessageBox : Window
    {
        public static BasicMessageBox Show(string content, string title = "")
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                var msgBox = new BasicMessageBox(title, content);
                msgBox.Show();
                return msgBox;
            });

        }

        public BasicMessageBox(string title, string content)
        {
            this.InitializeComponent();
            this.Title = title;
            this.textBlock.AppendText(content);
        }

        public void AddMessage(string message)
        {
            _ = this.textBlock.Dispatcher.InvokeAsync(() =>
            {
                this.textBlock.AppendText(message + "\n");
                this.scrollViewer.ScrollToEnd();
            });
        }

        public void Clear()
        {
            this.textBlock.Dispatcher.Invoke(() => this.textBlock.Document.Blocks.Clear());
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            this.Clear();
        }
    }
}

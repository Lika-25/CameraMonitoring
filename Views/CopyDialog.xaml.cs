using System.Windows;

namespace CameraMonitoring
{
    public partial class CopyDialog : Window
    {
        public CopyDialog(string address)
        {
            InitializeComponent();
            AddressTextBox.Text = address;
        }

        private void CopyAddress_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(AddressTextBox.Text); // Копирование в буфер обмена
            MessageBox.Show("Адреса скопійована!", "Інформація", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();       
        }
    }
}

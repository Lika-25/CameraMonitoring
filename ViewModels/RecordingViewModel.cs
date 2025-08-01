using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CameraMonitoring 
{
    public class RecordingViewModel : INotifyPropertyChanged
    {
        private string _statusText = "Ready 🟢";
        private Brush _statusColor = Brushes.Green;

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
                UpdateStatusColor(); // Обновление цвета при изменении текста
            }
        }

        public Brush StatusColor
        {
            get => _statusColor;
            private set
            {
                _statusColor = value;
                OnPropertyChanged();
            }
        }

        private void UpdateStatusColor()
        {
            if (StatusText == "Ready 🟢")
            {
                StatusColor = Brushes.Green;
            }
            else if (StatusText == "Rec 🔴")
            {
                StatusColor = Brushes.Red;
            }
            else if (StatusText == "Pause 🟡")
            {
                StatusColor = Brushes.Goldenrod;
            }
            else
            {
                StatusColor = Brushes.Black; // Цвет по умолчанию
            }
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Методы для изменения статуса
        public void StartRecording()
        {
            StatusText = "Rec 🔴";
        }

        public void PauseRecording()
        {
            StatusText = "Pause 🟡";
        }

        public void StopRecording()
        {
            StatusText = "Ready 🟢";
        }
    }
}

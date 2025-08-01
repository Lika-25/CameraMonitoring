using MimeKit;
using OpenCvSharp;
using System;
using System.Collections.ObjectModel;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CvRect = OpenCvSharp.Rect;
using OpenCvSharp.Extensions;


namespace CameraMonitoring
{
    public partial class MainWindow : System.Windows.Window
    {
        private VideoCapture _videoCapture;
        private VideoWriter _videoWriter;
        private Mat _previousFrame;
        private bool _isRecording = false;
        private bool _motionDetectionRunning = false;
        private bool _isPaused = false;
        private bool _notificationsEnabled = false;
        private string _currentFilePath;
        private int _currentCameraIndex = 0;

        private string _notificationEmail = "kapiba@ukr.net"; // Email для отправки уведомлений
        private string _selectedFolderPath;
        private HttpVideoServer _httpVideoServer;
        private string selectedFormat = "MP4"; // по умолчанию выбираем MP4
        private RecordingViewModel _viewModel;
        private CvRect _roi = new CvRect(0, 0, 640, 480); // Использование Rect из OpenCvSharp
                                                          // Зона интереса (Region of Interest)
        private double _motionSensitivity = 0.00000001;

        private Rectangle _selectionRectangle;
        private System.Windows.Point _startPoint;

        private DateTime _lastMotionTime; // Время последнего движения
        private TimeSpan _motionTimeout = TimeSpan.FromMinutes(1); // Таймаут для остановки записи

        public MainWindow()
        {
            InitializeComponent();
            DetectConnectedCameras();
            InitializeComboBox();
            LoadSaveDirectory();
            DetectConnectedCameras(); // Обнаруживаем камеры

            // Передаем индекс камеры (например, 0) в HttpVideoServer
            _httpVideoServer = new HttpVideoServer(0);
            // Встановлюємо камеру з індексом 0 за замовчуванням
            CamerasComboBox.SelectedIndex = 0;

            // Ініціалізуємо камеру з індексом 0
            OpenCamera(0);

            // Ініціалізуємо сервер для камери 0
            _httpVideoServer = new HttpVideoServer(0);

            this.Closing += OnWindowClosing;
            _viewModel = (RecordingViewModel)DataContext;
        }

        private void InitializeComboBox()
        {
            // Устанавливаем начальное значение в ComboBox
            foreach (var item in FormatComboBox.Items)
            {
                if (item is ComboBoxItem comboBoxItem && comboBoxItem.Content.ToString() == selectedFormat)
                {
                    FormatComboBox.SelectedItem = comboBoxItem;
                    break;
                }
            }
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isRecording)
            {
                _isRecording = false;
                _videoWriter?.Release();
                _videoWriter = null;
                UpdateMotionDetectionStatus("Запис завершено. Файл збережено.");
            }

            _videoCapture?.Release();
            _httpVideoServer?.StopServer();
        }

        // Метод детекции движения
        private bool DetectMotion()
        {
            if (_videoCapture == null || !_videoCapture.IsOpened())
                return false;

            Mat currentFrame = new Mat();
            _videoCapture.Read(currentFrame);

            if (currentFrame.Empty())
                return false;

            if (_previousFrame == null)
            {
                _previousFrame = currentFrame.Clone();
                return false;
            }

            try
            {
                Mat diffFrame = new Mat();
                Cv2.Absdiff(_previousFrame, currentFrame, diffFrame);

                Mat grayFrame = new Mat();
                Cv2.CvtColor(diffFrame, grayFrame, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(grayFrame, grayFrame, 25, 255, ThresholdTypes.Binary);

                double motionValue = Cv2.Sum(grayFrame).Val0 / (grayFrame.Rows * grayFrame.Cols);

                _previousFrame = currentFrame.Clone();

                return motionValue > _motionSensitivity * 255;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => UpdateMotionDetectionStatus($"Помилка детекції: {ex.Message}"));
                return false;
            }
        }





        private void StartRecordingOnMotion(object sender, RoutedEventArgs e)
        {
            if (_motionDetectionRunning)
            {
                UpdateMotionDetectionStatus("Функція запису з початком руху вже активна.");
                return;
            }

            if (_videoCapture == null || !_videoCapture.IsOpened())
            {
                UpdateMotionDetectionStatus("Камера не підключена.");
                return;
            }

            // Проверяем и устанавливаем ROI
            var frameWidth = _videoCapture.Get(VideoCaptureProperties.FrameWidth);
            var frameHeight = _videoCapture.Get(VideoCaptureProperties.FrameHeight);

            if (_roi.Width <= 0 || _roi.Height <= 0 || _roi.X < 0 || _roi.Y < 0 || _roi.X + _roi.Width > frameWidth || _roi.Y + _roi.Height > frameHeight)
            {
                _roi = new CvRect(0, 0, (int)frameWidth, (int)frameHeight);
                UpdateMotionDetectionStatus("Область руху не вибрано. Використовується весь кадр.");
            }

            _motionDetectionRunning = true;
            bool recording = false;

            Task.Run(() =>
            {
                UpdateMotionDetectionStatus("Детекція руху та запис активовані.");
                _lastMotionTime = DateTime.Now; // Инициализируем время последнего движения

                while (_motionDetectionRunning)
                {
                    if (DetectMotion()) // Если обнаружено движение
                    {
                        _lastMotionTime = DateTime.Now; // Обновляем время последнего движения

                        if (!recording)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                UpdateMotionDetectionStatus("Почато запис за рухом.");
                                StartRecording(null, null); // Запуск записи
                            });
                            recording = true;
                        }
                    }
                    else if (recording && DateTime.Now - _lastMotionTime >= _motionTimeout) // Если прошло больше минуты без движения
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StopRecording(null, null); // Остановка записи
                            UpdateMotionDetectionStatus("Запис зупинено через відсутність руху.");
                        });
                        recording = false;
                    }

                    Task.Delay(100).Wait(); // Задержка для оптимизации работы
                }
            });
        }


        private void StopRecordingOnMotion(object sender, RoutedEventArgs e)
        {
            _motionDetectionRunning = false;
            UpdateMotionDetectionStatus("Функцію запису за рухом вимкнено.");
        }

        private void StartRecording(object sender, RoutedEventArgs e)
        {
            _viewModel.StartRecording();

            if (_videoCapture == null)
            {
                OpenCamera(_currentCameraIndex);
            }

            // Если папка для сохранения не выбрана, показываем сообщение
            if (string.IsNullOrEmpty(_selectedFolderPath))
            {
                UpdateMotionDetectionStatus("Будь ласка, оберіть папку для збереження.");
                return;
            }

            // Создаем имя файла для записи с учетом выбранного формата
            string fileName = System.IO.Path.Combine(_selectedFolderPath, $"video_{DateTime.Now:yyyyMMdd_HHmmss}.{selectedFormat.ToLower()}");

            // Инициализация переменной для кодека
            int fourCC = 0;

            // Выбираем кодек в зависимости от выбранного формата
            switch (selectedFormat.ToUpper())
            {
                case "MP4":
                    fourCC = VideoWriter.FourCC('M', 'P', '4', 'V'); // Для MP4
                    break;

                case "AVI":
                    fourCC = VideoWriter.FourCC('X', 'V', 'I', 'D'); // Для AVI с кодеком XVID
                    break;

                case "MOV":
                    fourCC = VideoWriter.FourCC('M', 'J', 'P', 'G'); // Для MOV с кодеком MJPEG
                    break;

                case "MKV":
                    fourCC = VideoWriter.FourCC('X', '2', '6', '4'); // Для MKV с кодеком H264
                    break;

                case "FLV":
                    fourCC = VideoWriter.FourCC('F', 'L', 'V', '1'); // Для FLV с кодеком H263
                    break;

                case "WMV":
                    fourCC = VideoWriter.FourCC('W', 'M', 'V', '2'); // Для WMV
                    break;

                default:
                    UpdateMotionDetectionStatus("Невідомий формат! Використовуємо MP4 за замовчуванням.");
                    fourCC = VideoWriter.FourCC('M', 'P', '4', 'V'); // Для MP4 по умолчанию
                    break;
            }

            // Инициализация VideoWriter с выбранным кодеком
            _videoWriter = new VideoWriter(fileName, fourCC, 30, new OpenCvSharp.Size(640, 480), true);

            _isRecording = true;
            _isPaused = false;

            // Запуск процесса записи в отдельном потоке
            Task.Run(() =>
            {
                while (_isRecording)
                {
                    ProcessFrame();
                }
            });

            UpdateMotionDetectionStatus($"Запис розпочато. Відео буде збережено в: {fileName}");
        }


        private void PauseRecording(object sender, RoutedEventArgs e)
        {
            _viewModel.PauseRecording();

            if (!_isRecording)
            {
                UpdateMotionDetectionStatus("Запис ще не розпочато.");
                return;
            }

            _isPaused = !_isPaused; // Переключаем состояние паузы

            if (_isPaused)
            {
                UpdateMotionDetectionStatus("Запис на паузі.");
            }
            else
            {
                UpdateMotionDetectionStatus("Запис відновлено.");
            }
        }

        // Метод отправки email при обнаружении движения
        private async Task SendEmailNotification(string subject, string body)
        {
            if (string.IsNullOrEmpty(_notificationEmail))
            {
                UpdateMotionDetectionStatus("Email-адреса не встановлена.");
                return;
            }

            try
            {
                var mimeMessage = new MimeMessage
                {
                    Subject = subject,
                    Body = new TextPart("plain") { Text = body }
                };
                mimeMessage.From.Add(new MailboxAddress("Camera Monitoring", "kapiba@ukr.net"));
                mimeMessage.To.Add(new MailboxAddress("", _notificationEmail));

                using (var smtpClient = new MailKit.Net.Smtp.SmtpClient())
                {
                    await smtpClient.ConnectAsync("smtp.ukr.net", 465, true);
                    await smtpClient.AuthenticateAsync("kapiba@ukr.net", "XhAieyrU9ANzz4Ts");
                    await smtpClient.SendAsync(mimeMessage);
                    await smtpClient.DisconnectAsync(true);
                }

                UpdateMotionDetectionStatus("Email успішно відправлено.");
            }
            catch (Exception ex)
            {
                UpdateMotionDetectionStatus($"Помилка надсилання Email: {ex.Message}");
            }
        }

        private async Task SendEmailWithMailKit(MailMessage mailMessage)
        {
            try
            {
                var mimeMessage = new MimeMessage();
                mimeMessage.From.Add(new MailboxAddress("Lika", "kapiba@ukr.net"));
                mimeMessage.Subject = mailMessage.Subject;
                mimeMessage.Body = new TextPart("plain") { Text = mailMessage.Body };

                mimeMessage.To.Add(new MailboxAddress("", _notificationEmail));

                using (var smtpClient = new MailKit.Net.Smtp.SmtpClient())
                {
                    await smtpClient.ConnectAsync("smtp.ukr.net", 465, true);
                    await smtpClient.AuthenticateAsync("kapiba@ukr.net", "XhAieyrU9ANzz4Ts");
                    await smtpClient.SendAsync(mimeMessage);
                    await smtpClient.DisconnectAsync(true);
                }

                UpdateMotionDetectionStatus("Email отправлен успішно!");
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => UpdateMotionDetectionStatus($"Ошибка отправки email: {ex.Message}"));
            }
        }

        private void NotificationsEnabled(object sender, RoutedEventArgs e)
        {
            _notificationsEnabled = true;
            UpdateMotionDetectionStatus("Сповіщення увімкнено! Додайте email для отримання повідомлень.");

            // Открываем окно для ввода email
            var inputDialog = new InputDialog("Введіть вашу email-адресу:", _notificationEmail);
            if (inputDialog.ShowDialog() == true)
            {
                _notificationEmail = inputDialog.ResponseText;
                UpdateMotionDetectionStatus($"Сповіщення будуть надсилатися на {_notificationEmail}");
            }
        }

        private void NotificationsDisabled(object sender, RoutedEventArgs e)
        {
            _notificationsEnabled = false;
            UpdateMotionDetectionStatus("Сповіщення вимкнено!");
        }


        private void StopRecording(object sender, RoutedEventArgs e)
        {
            _viewModel.StopRecording();

            if (_isRecording)
            {
                _isRecording = false;
                _isPaused = false;
                _videoWriter?.Release();
                _videoWriter = null;

                UpdateMotionDetectionStatus($"Запис завершено. Файл збережено: {_currentFilePath}");
            }
        }


        private ObservableCollection<string> AvailableCameras { get; set; } = new ObservableCollection<string>();

        private void DetectConnectedCameras()
        {
            AvailableCameras.Clear();
            int maxCameras = 10;

            for (int i = 0; i < maxCameras; i++)
            {
                using (var tempCapture = new VideoCapture(i))
                {
                    if (tempCapture.IsOpened())
                    {
                        int width = Convert.ToInt32(tempCapture.Get(VideoCaptureProperties.FrameWidth));
                        int height = Convert.ToInt32(tempCapture.Get(VideoCaptureProperties.FrameHeight));
                        AvailableCameras.Add($"Камера {i}: {width}x{height}");
                    }
                }
            }

            CamerasComboBox.ItemsSource = AvailableCameras;

            if (AvailableCameras.Count > 0)
            {
                CamerasComboBox.SelectedIndex = 0; // Выбираем первую камеру
                CamerasComboBox_SelectionChanged(CamerasComboBox, null);
            }
            else
            {
                CameraInfo.Text = "Нема доступних камер.";
            }
        }




        // Обработчик для кнопки выбора зоны интереса
        private void SelectRoi_Click(object sender, RoutedEventArgs e)
        {
            // Показываем возможность рисования ROI
            UpdateMotionDetectionStatus("Виделіть зону руху на відео.");
        }


        private void RemoteMonitoringChecked(object sender, RoutedEventArgs e)
        {
            _httpVideoServer.StartServer();

            string streamAddress = "http://localhost:8080/";
            var copyDialog = new CopyDialog(streamAddress);
            copyDialog.Owner = this; // Устанавливаем родительское окно
            copyDialog.ShowDialog(); // Показываем окно как диалоговое
        }


        private void RemoteMonitoringUnchecked(object sender, RoutedEventArgs e)
        {
            UpdateMotionDetectionStatus("Віддалений моніторинг вимкнено!");
            _httpVideoServer.StopServer();
        }


        private void LoadSaveDirectory()
        {
            // Загружаем директорию, если она была сохранена
            _selectedFolderPath = Properties.Settings.Default.SaveDirectory;

            if (string.IsNullOrEmpty(_selectedFolderPath))
            {
                // Если директория не сохранена, попросим пользователя выбрать
                UpdateMotionDetectionStatus("Директорія для збереження ще не вибрана.");
                ChooseSaveLocation(null, null);
            }
            else
            {
                UpdateMotionDetectionStatus($"Директорія для збереження: {_selectedFolderPath}");
            }
        }

        private void ChooseSaveLocation(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Виберіть місце для збереження";
                folderDialog.RootFolder = Environment.SpecialFolder.MyComputer;

                var result = folderDialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    _selectedFolderPath = folderDialog.SelectedPath;

                    // Сохраняем выбранный путь в настройки
                    Properties.Settings.Default.SaveDirectory = _selectedFolderPath;
                    Properties.Settings.Default.Save();  // Обязательно вызываем Save, чтобы изменения были сохранены

                    UpdateMotionDetectionStatus($"Місце збереження обрано: {_selectedFolderPath}");
                }
                else
                {
                    UpdateMotionDetectionStatus("Не вибрано місце для збереження.");
                }
            }
        }


        private void ViewRecordedVideos(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFolderPath))
            {
                UpdateMotionDetectionStatus("Місце для збереження не вибрано.");
                return;
            }

            System.Diagnostics.Process.Start("explorer.exe", _selectedFolderPath);
        }

        private async void OpenCamera(int cameraIndex)
        {
            // Освобождаем предыдущую камеру, если она была открыта
            _videoCapture?.Release();
            _videoCapture = new VideoCapture(cameraIndex);

            if (!_videoCapture.IsOpened())
            {
                CameraInfo.Text = "Камера не підключена.";
                return;
            }

            // Получение параметров камеры
            int frameWidth = Convert.ToInt32(_videoCapture.Get(VideoCaptureProperties.FrameWidth));
            int frameHeight = Convert.ToInt32(_videoCapture.Get(VideoCaptureProperties.FrameHeight));
            double fps = _videoCapture.Get(VideoCaptureProperties.Fps);

            // Устанавливаем FPS вручную, если значение 0.0
            if (fps <= 0)
            {
                fps = 30.0; // FPS по умолчанию
                _videoCapture.Set(VideoCaptureProperties.Fps, fps); // Установка FPS (не все камеры поддерживают)
            }

            // Устанавливаем размеры окна для отображения
            VideoImage.Width = frameWidth;
            VideoImage.Height = frameHeight;

            CameraInfo.Text = $"Камера {cameraIndex}: {frameWidth}x{frameHeight}, {fps:F1} FPS";

            // Запускаем обновление кадров
            await StartFrameUpdateAsync(fps);
        }


        private async Task StartFrameUpdateAsync(double fps)
        {
            bool isReconnecting = false; // Флаг для состояния переподключения

            try
            {
                while (_videoCapture != null && _videoCapture.IsOpened())
                {
                    Mat frame = new Mat();
                    bool success = _videoCapture.Read(frame);

                    if (!success || frame.Empty())
                    {
                        // Если кадр пустой, обрабатываем переподключение
                        if (!isReconnecting)
                        {
                            isReconnecting = true;
                            CameraInfo.Text = "Помилка читання кадру. Спроба перепідключення...";
                        }

                        // Переподключение к камере
                        _videoCapture.Release();
                        _videoCapture = new VideoCapture(0); // Индекс вашей камеры
                        await Task.Delay(100); // Задержка перед повторным подключением
                        continue;
                    }

                    // Если кадр успешно получен, снимаем флаг переподключения
                    if (isReconnecting)
                    {
                        isReconnecting = false;

                        // Обновляем информацию о камере
                        int frameWidth = Convert.ToInt32(_videoCapture.Get(VideoCaptureProperties.FrameWidth));
                        int frameHeight = Convert.ToInt32(_videoCapture.Get(VideoCaptureProperties.FrameHeight));
                        double newFps = _videoCapture.Get(VideoCaptureProperties.Fps);

                        if (newFps > 0)
                            fps = newFps;

                        CameraInfo.Text = $"Камеру успішно підключено: {frameWidth}x{frameHeight}, {fps:F1} FPS";
                    }

                    // Преобразование кадра в Bitmap
                    var image = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame);

                    // Обновление UI
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        VideoImage.Source = BitmapSourceConverter.BitmapToImageSource(image);
                    });

                    // Рассчитываем задержку для корректного FPS
                    int delay = (int)(1000 / fps);
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                CameraInfo.Text = $"Помилка оновлення кадрів: {ex.Message}";
            }
        }


        private void ProcessFrame()
        {
            if (_videoCapture != null && _videoCapture.IsOpened())
            {
                Mat frame = new Mat();
                _videoCapture.Read(frame);

                if (_isRecording && _videoWriter != null && !_isPaused)
                {
                    _videoWriter.Write(frame);
                }

                Dispatcher.Invoke(() =>
                {
                    VideoImage.Source = frame.ToBitmapSource();
                });
            }
        }

        private void CamerasComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CamerasComboBox.SelectedIndex >= 0)
            {
                try
                {
                    // Освобождаем предыдущую камеру
                    _videoCapture?.Release();
                    _videoCapture = new VideoCapture(CamerasComboBox.SelectedIndex);

                    if (_videoCapture.IsOpened())
                    {
                        // Получаем параметры камеры
                        int frameWidth = Convert.ToInt32(_videoCapture.Get(VideoCaptureProperties.FrameWidth));
                        int frameHeight = Convert.ToInt32(_videoCapture.Get(VideoCaptureProperties.FrameHeight));
                        double fps = _videoCapture.Get(VideoCaptureProperties.Fps);

                        // Если FPS равно 0.0, задаем его вручную
                        if (fps <= 0)
                        {
                            fps = 30.0; // Устанавливаем значение по умолчанию
                            _videoCapture.Set(VideoCaptureProperties.Fps, fps); // Попытка установки (не всегда поддерживается)
                        }

                        // Выводим информацию о камере
                        CameraInfo.Text = $"Обрано камеру {CamerasComboBox.SelectedIndex}: {frameWidth}x{frameHeight}, FPS: {fps:F1}";

                        // Обновляем сервер для новой камеры
                        _httpVideoServer.ChangeCamera(CamerasComboBox.SelectedIndex);

                        // Запускаем просмотр видео
                        StartFrameUpdate();
                    }
                    else
                    {
                        CameraInfo.Text = $"Не вдалося відкрити камеру {CamerasComboBox.SelectedIndex}.";
                    }
                }
                catch (Exception ex)
                {
                    CameraInfo.Text = $"Помилка: {ex.Message}";
                }
            }
        }


        private async void StartFrameUpdate()
        {
            if (_videoCapture == null || !_videoCapture.IsOpened())
            {
                CameraInfo.Text = "Камера не активна.";
                return;
            }

            try
            {
                Mat frame = new Mat();

                while (_videoCapture.IsOpened())
                {
                    // Захват кадра
                    _videoCapture.Read(frame);

                    if (!frame.Empty())
                    {
                        // Обновление UI-потока с проверкой
                        Dispatcher.Invoke(() =>
                        {
                            VideoImage.Source = frame.ToBitmapSource();
                        }, System.Windows.Threading.DispatcherPriority.Render);
                    }

                    // Задержка для регулировки FPS
                    await Task.Delay(33); // Около 30 FPS
                }

                // Очистка ресурсов после завершения
                frame.Dispose();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    CameraInfo.Text = $"Помилка: {ex.Message}";
                });
            }
        }
        private void SensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _motionSensitivity = e.NewValue;
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Запоминаем начальную точку, где нажата мышь
            _startPoint = e.GetPosition(Canvas);

            // Создаем новый прямоугольник для рисования
            _selectionRectangle = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 0, 0)) // полупрозрачный красный
            };

            // Добавляем прямоугольник на Canvas
            Canvas.Children.Add(_selectionRectangle);

            // Устанавливаем позицию прямоугольника
            Canvas.SetLeft(_selectionRectangle, _startPoint.X);
            Canvas.SetTop(_selectionRectangle, _startPoint.Y);
        }

        private void Canvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _selectionRectangle != null)
            {
                // Получаем текущую точку мыши
                System.Windows.Point currentWpfPoint = e.GetPosition(Canvas);

                // Преобразуем в OpenCvSharp.Point
                OpenCvSharp.Point currentPoint = new OpenCvSharp.Point(currentWpfPoint.X, currentWpfPoint.Y);

                // Вычисляем ширину и высоту прямоугольника
                double width = currentPoint.X - _startPoint.X;
                double height = currentPoint.Y - _startPoint.Y;

                // Обновляем размер и позицию прямоугольника
                _selectionRectangle.Width = Math.Abs(width);
                _selectionRectangle.Height = Math.Abs(height);

                if (width < 0)
                    Canvas.SetLeft(_selectionRectangle, currentPoint.X);
                if (height < 0)
                    Canvas.SetTop(_selectionRectangle, currentPoint.Y);
            }
        }


        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_selectionRectangle != null)
            {
                // Сохраняем координаты и размеры прямоугольника
                _roi = new CvRect(
                    (int)Canvas.GetLeft(_selectionRectangle),
                    (int)Canvas.GetTop(_selectionRectangle),
                    (int)_selectionRectangle.Width,
                    (int)_selectionRectangle.Height);

                UpdateMotionDetectionStatus($"Зону руху встановлено: {_roi.X}, {_roi.Y}, {_roi.Width}, {_roi.Height}");

                // Убираем прямоугольник с Canvas
                Canvas.Children.Remove(_selectionRectangle);
                _selectionRectangle = null;
            }
        }


        private void FormatComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Когда пользователь выбирает формат записи
            if (FormatComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                selectedFormat = selectedItem.Content.ToString();
            }
        }



        private void StopMotionDetection()
        {
            if (!_motionDetectionRunning)
            {
                UpdateMotionDetectionStatus("Детекцію руху не запущено.");
                return;
            }

            _motionDetectionRunning = false;
            UpdateMotionDetectionStatus("Детекцію руху зупинено.");
        }


        private void StartMotionDetectionWithEmail()
        {
            if (_motionDetectionRunning)
            {
                UpdateMotionDetectionStatus("Детекція руху вже включено.");
                return;
            }

            if (_videoCapture == null || !_videoCapture.IsOpened())
            {
                UpdateMotionDetectionStatus("Камера не підключена.");
                return;
            }

            _motionDetectionRunning = true;
            _previousFrame = null;

            Task.Run(async () =>
            {
                while (_motionDetectionRunning)
                {
                    if (DetectMotion() && _notificationsEnabled)
                    {
                        string subject = "Рух виявлено!";
                        string body = $"Детектор руху зафіксував рух у {DateTime.Now:dd-MM-yyyy HH:mm:ss}.";
                        await SendEmailNotification(subject, body); // Добавляем await
                    }


                    await Task.Delay(500); // Задержка между проверками
                }
            });

            UpdateMotionDetectionStatus("Детекция руху с відправкою сповіщень увімкнено.");
        }

        private void DisableMotionDetection(object sender, RoutedEventArgs e)
        {
            StopMotionDetection(); // Останавливаем детекцию
        }

        private void StartMotionDetectionWithMessage()
        {
            if (_motionDetectionRunning)
            {
                UpdateMotionDetectionStatus("Детекцию руху вже включено.");
                return;
            }

            if (_videoCapture == null || !_videoCapture.IsOpened())
            {
                UpdateMotionDetectionStatus("Камера не підключена.");
                return;
            }

            _motionDetectionRunning = true;
            _previousFrame = null;

            Task.Run(() =>
            {
                while (_motionDetectionRunning)
                {
                    if (DetectMotion()) // Если движение обнаружено
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateMotionDetectionStatus("Виявлено рух!"); // Показываем сообщение
                        });
                    }

                    Task.Delay(500).Wait(); // Задержка между проверками
                }
            });

            UpdateMotionDetectionStatus("Детекція руху з виведенням повідомлень увімкнена.");
        }
        // Метод для отключения детекции движения




        // Включение детекции движения с выводом сообщений
        private void EnableMotionDetectionForMessages(object sender, RoutedEventArgs e)
        {
            StartMotionDetectionWithMessage(); // Включаем детекцию с выводом сообщений
        }

        // Выключение детекции движения
        private void EnableEmailNotifications(object sender, RoutedEventArgs e)
        {
            _notificationsEnabled = true;
            UpdateMotionDetectionStatus("Сповіщення увімкнено.");

            // Опционально: запрос email у пользователя, если не установлен

            var inputDialog = new InputDialog("Введіть вашу email-адресу:", _notificationEmail);
            if (inputDialog.ShowDialog() == true)
            {
                _notificationEmail = inputDialog.ResponseText;
                UpdateMotionDetectionStatus($"Сповіщення будуть надсилатися на {_notificationEmail}.");

                StartMotionDetectionWithEmail(); // Включаем детекцию с отправкой email
            }

        }

        private void DisableEmailNotifications(object sender, RoutedEventArgs e)
        {
            _notificationsEnabled = false;
            UpdateMotionDetectionStatus("Сповіщення вимкнено.");
        }
        private void UpdateMotionDetectionStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                MotionDetectionStatus.Text = message; // Обновляем текст в TextBlock
            });
        }


    }
}

using OpenCvSharp;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CameraMonitoring
{
    public class HttpVideoServer
    {
        private HttpListener _httpListener;
        private VideoCapture _videoCapture;
        private readonly object _lock = new object(); // Lock for thread safety

        public HttpVideoServer(int cameraIndex = 0)
        {
            _videoCapture = new VideoCapture(cameraIndex);
            if (!_videoCapture.IsOpened())
            {
                throw new Exception($"Failed to open camera with index {cameraIndex}");
            }
        }

        public void StartServer(string address = "http://+:8080/")
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(address);
            _httpListener.Start();
            Task.Run(() =>
            {
                while (_httpListener.IsListening)
                {
                    try
                    {
                        var context = _httpListener.GetContext();
                        Task.Run(() => ProcessRequest(context));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    }
                }
            });

            Console.WriteLine($"Сервер запущено! Потік доступний за адресою: {address}");
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            try
            {
                context.Response.ContentType = "multipart/x-mixed-replace; boundary=--frame";
                context.Response.StatusCode = 200;
                var responseStream = context.Response.OutputStream;

                while (_httpListener.IsListening)
                {
                    Mat frame;
                    lock (_lock)
                    {
                        frame = new Mat();
                        _videoCapture.Read(frame);
                    }

                    if (!frame.Empty())
                    {
                        var imageData = frame.ToBytes(".jpg");
                        var header = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {imageData.Length}\r\n\r\n";
                        var headerBytes = Encoding.UTF8.GetBytes(header);

                        await responseStream.WriteAsync(headerBytes, 0, headerBytes.Length);
                        await responseStream.WriteAsync(imageData, 0, imageData.Length);
                        await responseStream.WriteAsync(new byte[] { 13, 10 }, 0, 2);
                    }

                    await Task.Delay(33); // ~30 FPS
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while streaming: {ex.Message}");
            }
            finally
            {
                context.Response.Close();
            }
        }

        public void ChangeCamera(int cameraIndex)
        {
            try
            {
                var newCapture = new VideoCapture(cameraIndex);
                if (!newCapture.IsOpened())
                {
                    throw new Exception($"Не вдалося відкрити камеру {cameraIndex}");
                }

                lock (_lock)
                {
                    _videoCapture?.Release();
                    _videoCapture = newCapture;
                }

                Console.WriteLine($"Камера переключена на індекс {cameraIndex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка при зміні камери: {ex.Message}");
            }
        }

        public void StopServer()
        {
            _httpListener?.Stop();
            _httpListener = null;
            lock (_lock)
            {
                _videoCapture?.Release();
            }
            Console.WriteLine("Сервер зупинено.");
        }
    }
}

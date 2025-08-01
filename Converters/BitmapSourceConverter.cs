using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CameraMonitoring
{
    public static class BitmapSourceConverter
    {
        public static BitmapSource ToBitmapSource(this OpenCvSharp.Mat mat)
        {
            return BitmapSource.Create(
                mat.Width, mat.Height,
                96, 96,
                System.Windows.Media.PixelFormats.Bgr24,
                null,
                mat.Data,
                (int)(mat.Step() * mat.Rows),
                (int)mat.Step()
            );
        }

        public static ImageSource BitmapToImageSource(Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();

                return bitmapImage;
            }
        }
    }
}

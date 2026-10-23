using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TaskList.App;

/// <summary>
/// Frosted glass drawn by the app itself: the desktop wallpaper, heavily blurred, placed so the part
/// under the window lines up with the real desktop. Windows' own acrylic blur isn't available on
/// battery on some laptops, so this gives the same look everywhere. Moving the window only shifts the
/// picture; the blur is computed once per wallpaper and monitor.
/// </summary>
public sealed class WallpaperGlass
{
    private readonly Image _image;
    private readonly AppWindow _window;
    private RectInt32Like _monitor;
    private string? _loadedKey;

    private record struct RectInt32Like(int X, int Y, int Width, int Height);

    public WallpaperGlass(Image image, AppWindow window)
    {
        _image = image;
        _window = window;
    }

    /// <summary>Loads (or reloads, if the monitor or wallpaper changed) and positions the blurred wallpaper.</summary>
    public async Task RefreshAsync(double scale)
    {
        var area = DisplayArea.GetFromWindowId(_window.Id, DisplayAreaFallback.Nearest).OuterBounds;
        var monitor = new RectInt32Like(area.X, area.Y, area.Width, area.Height);
        var path = WallpaperPath();
        var key = $"{path}|{File.GetLastWriteTimeUtc(path ?? "")}|{monitor}";
        if (path != null && key != _loadedKey)
        {
            var source = await BlurAsync(path, monitor.Width, monitor.Height);
            if (source != null)
            {
                _image.Source = source;
                _loadedKey = key;
            }
        }
        _monitor = monitor;
        Position(scale);
    }

    /// <summary>Shifts the picture so it stays aligned with the desktop as the window moves.</summary>
    public void Position(double scale)
    {
        if (_monitor.Width == 0) return;
        _image.Width = _monitor.Width / scale;
        _image.Height = _monitor.Height / scale;
        Canvas.SetLeft(_image, (_monitor.X - _window.Position.X) / scale);
        Canvas.SetTop(_image, (_monitor.Y - _window.Position.Y) / scale);
    }

    private static string? WallpaperPath()
    {
        var buffer = new char[520];
        if (SystemParametersInfo(0x0073, (uint)buffer.Length, buffer, 0)) // SPI_GETDESKWALLPAPER
        {
            var path = new string(buffer).TrimEnd('\0');
            if (File.Exists(path)) return path;
        }
        var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }

    /// <summary>
    /// Scales the wallpaper to fill the monitor (as Windows' default "Fill" does), shrinks it a lot, and
    /// box-blurs it. Shown stretched back up, the tiny image reads as a soft, smooth frost.
    /// </summary>
    private static async Task<BitmapImage?> BlurAsync(string path, int monitorWidth, int monitorHeight)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);

            const int targetWidth = 160;
            int targetHeight = Math.Max(1, targetWidth * monitorHeight / monitorWidth);
            double cover = Math.Max((double)targetWidth / decoder.PixelWidth, (double)targetHeight / decoder.PixelHeight);
            uint scaledW = (uint)Math.Ceiling(decoder.PixelWidth * cover), scaledH = (uint)Math.Ceiling(decoder.PixelHeight * cover);
            var transform = new BitmapTransform
            {
                ScaledWidth = scaledW,
                ScaledHeight = scaledH,
                InterpolationMode = BitmapInterpolationMode.Fant,
                Bounds = new BitmapBounds
                {
                    X = (scaledW - (uint)targetWidth) / 2,
                    Y = (scaledH - (uint)targetHeight) / 2,
                    Width = (uint)targetWidth,
                    Height = (uint)targetHeight,
                },
            };
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
            var data = pixels.DetachPixelData();

            for (int pass = 0; pass < 3; pass++) BoxBlur(data, targetWidth, targetHeight, 4);

            var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)targetWidth, (uint)targetHeight, 96, 96, data);
            await encoder.FlushAsync();
            output.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(output);
            return image;
        }
        catch (Exception)
        {
            return null; // No wallpaper (solid color or slideshow edge cases): the veil alone still looks fine.
        }
    }

    /// <summary>Separable box blur on a BGRA buffer, with clamped edges.</summary>
    private static void BoxBlur(byte[] data, int w, int h, int r)
    {
        var tmp = new byte[data.Length];
        int window = r * 2 + 1;
        for (int y = 0; y < h; y++)
        {
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int k = -r; k <= r; k++) sum += data[(y * w + Math.Clamp(k, 0, w - 1)) * 4 + c];
                for (int x = 0; x < w; x++)
                {
                    tmp[(y * w + x) * 4 + c] = (byte)(sum / window);
                    sum += data[(y * w + Math.Min(x + r + 1, w - 1)) * 4 + c] - data[(y * w + Math.Max(x - r, 0)) * 4 + c];
                }
            }
        }
        for (int x = 0; x < w; x++)
        {
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int k = -r; k <= r; k++) sum += tmp[(Math.Clamp(k, 0, h - 1) * w + x) * 4 + c];
                for (int y = 0; y < h; y++)
                {
                    data[(y * w + x) * 4 + c] = (byte)(sum / window);
                    sum += tmp[(Math.Min(y + r + 1, h - 1) * w + x) * 4 + c] - tmp[(Math.Max(y - r, 0) * w + x) * 4 + c];
                }
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint action, uint param, char[] buffer, uint winIni);
}

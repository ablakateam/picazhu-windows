using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Picazhu.Core;

namespace Picazhu.App;

public partial class PreviewWindow : Window
{
    private readonly QuickPreviewResult _preview;

    public PreviewWindow(QuickPreviewResult preview)
    {
        _preview = preview;
        InitializeComponent();
        Loaded += PreviewWindow_Loaded;

        if (preview.MediaKind == MediaKind.Video && File.Exists(preview.FullPath))
        {
            PreviewVideo.Visibility = Visibility.Visible;
            return;
        }

        PreviewImage.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(preview.ThumbnailPath) && File.Exists(preview.ThumbnailPath))
        {
            PreviewImage.Source = LoadBitmap(preview.ThumbnailPath);
        }
    }

    private void PreviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_preview.MediaKind != MediaKind.Video || !File.Exists(_preview.FullPath))
        {
            return;
        }

        PreviewVideo.Source = new Uri(_preview.FullPath);
        PreviewVideo.Position = TimeSpan.Zero;
        PreviewVideo.Play();
    }

    private void PreviewVideo_MediaOpened(object sender, RoutedEventArgs e)
    {
        PreviewVideo.Position = TimeSpan.Zero;
        PreviewVideo.Play();
    }

    private void PreviewVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        PreviewVideo.Stop();
        PreviewVideo.Source = null;
        PreviewVideo.Visibility = Visibility.Collapsed;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewImage.Source = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        PreviewVideo.Stop();
        PreviewVideo.Close();
        base.OnClosed(e);
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }
}

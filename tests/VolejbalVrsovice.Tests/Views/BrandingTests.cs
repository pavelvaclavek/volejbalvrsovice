using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VolejbalVrsovice.App;
using VolejbalVrsovice.App.Views;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Views
{
    /// <summary>Branding podle zadání, sekce 4: logo, emblém, ikona a barvy převzaté z loga.</summary>
    public class BrandingTests
    {
        private const string AssetsRoot = "/VolejbalVrsovice;component/Assets/";

        [Fact]
        public void Color_scheme_uses_the_colors_of_the_club_logo()
        {
            WpfTestHost.Run(() =>
            {
                Assert.Equal(Color.FromRgb(0x00, 0x2E, 0x5D), Brush("NavyBrush"));     // tmavě modrá z loga
                Assert.Equal(Color.FromRgb(0x00, 0x85, 0xE9), Brush("AccentBrush"));   // světle modrá z loga
                Assert.Equal(Colors.White, Brush("WhiteBrush"));
                Assert.Equal(Color.FromRgb(0xC6, 0x28, 0x28), Brush("DangerBrush"));   // červená jen pro chyby a záporné saldo
            });
        }

        [Fact]
        public void Horizontal_logo_is_120_px_high_and_optimized()
        {
            var bytes = ReadAsset("logo-horizontal.png");
            var image = Decode(bytes);

            Assert.Equal(120, image.PixelHeight);
            Assert.InRange(image.PixelWidth, 308, 316);     // poměr stran originálu 2712 × 1044
            Assert.True(bytes.Length <= 30 * 1024, "logo-horizontal.png má " + bytes.Length + " B");
        }

        [Fact]
        public void Emblem_is_a_256_px_square_with_transparent_background()
        {
            var bytes = ReadAsset("logo-emblem.png");
            var image = Decode(bytes);

            Assert.Equal(256, image.PixelWidth);
            Assert.Equal(256, image.PixelHeight);
            Assert.True(bytes.Length <= 50 * 1024, "logo-emblem.png má " + bytes.Length + " B");

            // Rohy jsou průhledné, střed (hlava dravce) je neprůhledný.
            Assert.Equal(0, Alpha(image, 0, 0));
            Assert.Equal(0, Alpha(image, 255, 0));
            Assert.Equal(255, Alpha(image, 128, 100));
        }

        [Fact]
        public void Application_icon_contains_sizes_16_32_48_and_256()
        {
            var bytes = ReadAsset("app.ico");

            using (var stream = new MemoryStream(bytes))
            {
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var sizes = decoder.Frames.Select(frame => frame.PixelWidth).OrderBy(size => size).ToArray();

                Assert.Equal(new[] { 16, 32, 48, 256 }, sizes);
                Assert.All(decoder.Frames, frame => Assert.Equal(frame.PixelWidth, frame.PixelHeight));
            }

            Assert.True(bytes.Length <= 50 * 1024, "app.ico má " + bytes.Length + " B");
        }

        [Fact]
        public void Every_window_shows_the_emblem_as_its_icon()
        {
            WpfTestHost.Run(() =>
            {
                var windows = new Window[]
                {
                    new LoginWindow(), new MainWindow(), new ChangePasswordDialog(), new RecoveryCodeDialog(),
                    new ForgotPasswordDialog(), new AddUserDialog(), new ResetPasswordDialog(),
                };

                foreach (var window in windows)
                {
                    Assert.True(window.Icon != null, window.GetType().Name + " nemá ikonu");
                    window.Close();
                }
            });
        }

        [Fact]
        public void Version_shown_in_the_application_is_plain_x_y_z()
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
            Assert.Equal("Verze " + AppInfo.Version, AppInfo.VersionText);
        }

        private static Color Brush(string key)
        {
            return ((SolidColorBrush)Application.Current.FindResource(key)).Color;
        }

        private static byte[] ReadAsset(string name)
        {
            return WpfTestHost.Run(() =>
            {
                var resource = Application.GetResourceStream(new Uri(AssetsRoot + name, UriKind.Relative));
                Assert.NotNull(resource);

                using (var stream = resource.Stream)
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            });
        }

        private static BitmapSource Decode(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }

        private static byte Alpha(BitmapSource image, int x, int y)
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var pixel = new byte[4];
            converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return pixel[3];
        }
    }
}

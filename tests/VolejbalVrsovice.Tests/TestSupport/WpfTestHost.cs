using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VolejbalVrsovice.Tests.TestSupport
{
    /// <summary>
    /// Jedno STA vlákno s instancí aplikace (kvůli stylům z App.xaml) pro testy, které vytvářejí okna.
    /// WPF dovoluje jen jednu <see cref="Application"/> na proces, proto je hostitel sdílený.
    /// </summary>
    internal static class WpfTestHost
    {
        private static readonly Lazy<Dispatcher> Host = new Lazy<Dispatcher>(Start, LazyThreadSafetyMode.ExecutionAndPublication);

        public static void Run(Action action)
        {
            Host.Value.Invoke(action);
        }

        public static T Run<T>(Func<T> function)
        {
            return Host.Value.Invoke(function);
        }

        private static Dispatcher Start()
        {
            Dispatcher dispatcher = null;
            Exception failure = null;

            using (var ready = new ManualResetEventSlim())
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        var application = new VolejbalVrsovice.App.App();
                        application.InitializeComponent();   // načte App.xaml včetně slovníků stylů
                        dispatcher = Dispatcher.CurrentDispatcher;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }

                    ready.Set();

                    if (failure == null)
                    {
                        Dispatcher.Run();
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = "WPF test host";
                thread.Start();
                ready.Wait();
            }

            if (failure != null)
            {
                throw new InvalidOperationException("Hostitel WPF se nepodařilo spustit.", failure);
            }

            return dispatcher;
        }
    }

    /// <summary>Výsledek vykreslení jednoho okna.</summary>
    internal sealed class RenderedView
    {
        public RenderedView(string name, BitmapSource image, IReadOnlyList<string> bindingErrors)
        {
            Name = name;
            Image = image;
            BindingErrors = bindingErrors;
        }

        public string Name { get; }

        public BitmapSource Image { get; }

        public IReadOnlyList<string> BindingErrors { get; }

        /// <summary>Počet různých barev ve vzorku pixelů – prázdné (jednobarevné) okno má 1.</summary>
        public int CountDistinctColors()
        {
            var stride = Image.PixelWidth * 4;
            var pixels = new byte[stride * Image.PixelHeight];
            Image.CopyPixels(pixels, stride, 0);

            var colors = new HashSet<int>();
            for (var offset = 0; offset < pixels.Length; offset += 4 * 7)
            {
                colors.Add(BitConverter.ToInt32(pixels, offset));
            }

            return colors.Count;
        }

        public void SaveTo(string directory)
        {
            Directory.CreateDirectory(directory);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Image));
            using (var stream = File.Create(Path.Combine(directory, Name + ".png")))
            {
                encoder.Save(stream);
            }
        }
    }

    /// <summary>
    /// Vykreslí okno do bitmapy, aniž by ho uživatel viděl: okno se otevře daleko mimo obrazovku,
    /// bez aktivace a bez položky na hlavním panelu.
    /// </summary>
    internal static class ViewRenderer
    {
        public static RenderedView Render(string name, Func<Window> createWindow)
        {
            return WpfTestHost.Run(() =>
            {
                var listener = new BindingErrorListener();
                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);

                Window window = null;
                try
                {
                    window = createWindow();
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = -30000;
                    window.Top = -30000;
                    window.ShowActivated = false;
                    window.ShowInTaskbar = false;
                    window.Show();
                    window.UpdateLayout();

                    // Nechat doběhnout vše, co si WPF naplánovalo (vazby, validace, Loaded).
                    window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

                    var root = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
                    var bitmap = new RenderTargetBitmap(
                        Math.Max(1, (int)Math.Ceiling(root.ActualWidth)),
                        Math.Max(1, (int)Math.Ceiling(root.ActualHeight)),
                        96, 96, PixelFormats.Pbgra32);

                    // Bílý podklad – průhledné okraje by v PNG vypadaly jako černé.
                    var background = new DrawingVisual();
                    using (var context = background.RenderOpen())
                    {
                        context.DrawRectangle(Brushes.White, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                    }

                    bitmap.Render(background);
                    bitmap.Render(root);
                    bitmap.Freeze();

                    return new RenderedView(name, bitmap, listener.Messages.ToArray());
                }
                finally
                {
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                    if (window != null)
                    {
                        window.Close();
                    }
                }
            });
        }

        private sealed class BindingErrorListener : TraceListener
        {
            public List<string> Messages { get; } = new List<string>();

            public override void Write(string message)
            {
            }

            public override void WriteLine(string message)
            {
                Messages.Add(message);
            }
        }
    }
}

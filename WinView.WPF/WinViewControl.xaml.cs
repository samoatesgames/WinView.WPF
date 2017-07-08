using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinView.WPF
{
    /// <summary>
    /// Interaction logic for WinViewControl.xaml
    /// </summary>
    public partial class WinViewControl
    {
        /// <summary>
        /// The maximum update rate in milliseconds.
        /// Default is 60fps.
        /// TODO: Make this a dependency property.
        /// </summary>
        private const int c_updateRate = 1000 / 60;

        /// <summary>
        /// The task used to call the real-time window capturing.
        /// </summary>
        private readonly Task m_captureTask;

        /// <summary>
        /// The cancellation token for the capture task.
        /// </summary>
        private readonly CancellationTokenSource m_cancellationTokenSource;

        /// <summary>
        /// Default constructor
        /// </summary>
        public WinViewControl()
        {
            InitializeComponent();

            m_cancellationTokenSource = new CancellationTokenSource();
            m_captureTask = new Task(CaptureWindow, m_cancellationTokenSource.Token);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Called when the control is unloaded.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="routedEventArgs"></param>
        private void OnUnloaded(object sender, RoutedEventArgs routedEventArgs)
        {
            m_cancellationTokenSource.Cancel();
        }

        /// <summary>
        /// Called when the control is loaded
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="routedEventArgs"></param>
        private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            m_captureTask.Start();
        }

        /// <summary>
        /// Called by the capture task.
        /// This is where the window is captured and copied to the render image.
        /// </summary>
        private void CaptureWindow()
        {
            var token = m_cancellationTokenSource.Token;
            var dispatcher = Application.Current.Dispatcher;
            var bitmapSizeOptions = BitmapSizeOptions.FromEmptyOptions();

            var captureHandle = User32.GetDesktopWindow();  // TODO: This should be a dependency property
            using (var captureWindow = new WindowCapture(captureHandle))
            {
                while (!token.IsCancellationRequested)
                {
                    var startTime = DateTime.Now;
                    captureWindow.CaptureWindow(
                        dc =>
                        {
                            // Jump back to the application dispatcher to set the actual image source
                            dispatcher.Invoke(() =>
                            {
                                RenderImage.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                        dc, IntPtr.Zero, Int32Rect.Empty, bitmapSizeOptions
                                    );
                            }, DispatcherPriority.Render, token);
                        }
                    );
                    var captureTime = DateTime.Now - startTime;

                    // Offset wait time with the amount of time it took to capture.
                    // This means we are limited to the update rate, but if capturing
                    // takes longer than a 'single frame' we don't stall.
                    var wait = c_updateRate - captureTime.Milliseconds;
                    while ((--wait) >= 0)
                    {
                        Thread.Sleep(1);
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Wrapper around GDI window capturing.
        /// </summary>
        internal class WindowCapture : IDisposable
        {
            private readonly Win32Rect m_windowRect;
            private readonly IntPtr m_sourceDc;
            private readonly IntPtr m_targetDc;
            private readonly IntPtr m_compatibleBitmapHandle;

            /// <summary>
            /// Initialize all Gdi resources.
            /// </summary>
            /// <param name="windowHandle"></param>
            public WindowCapture(IntPtr windowHandle)
            {
                User32.GetWindowRect(windowHandle, out m_windowRect);
                m_sourceDc = User32.GetDC(windowHandle);
                m_targetDc = Gdi32.CreateCompatibleDC(m_sourceDc);
                m_compatibleBitmapHandle = Gdi32.CreateCompatibleBitmap(m_sourceDc, m_windowRect.Width, m_windowRect.Height);
                Gdi32.SelectObject(m_targetDc, m_compatibleBitmapHandle);
            }

            /// <summary>
            /// Destory all Gdi resources.
            /// </summary>
            public void Dispose()
            {
                Gdi32.DeleteObject(m_compatibleBitmapHandle);
                User32.ReleaseDC(IntPtr.Zero, m_sourceDc);
                User32.ReleaseDC(IntPtr.Zero, m_targetDc);
            }

            /// <summary>
            /// Copy the target windows contents to a bitmap handle.
            /// Then call the callback with the address of the handle.
            /// </summary>
            /// <param name="callback"></param>
            public void CaptureWindow(Action<IntPtr> callback)
            {
                try
                {
                    if (Gdi32.BitBlt(
                        m_targetDc,
                        0, 0,
                        m_windowRect.Width, m_windowRect.Height,
                        m_sourceDc,
                        0, 0,
                        Gdi32.Srccopy))
                    {
                        callback(m_compatibleBitmapHandle);
                    }
                }
                catch(Exception)
                {
                    // Ignored
                }
            }
        }
    }
}

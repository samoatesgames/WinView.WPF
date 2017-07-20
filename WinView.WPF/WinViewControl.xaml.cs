using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinView.WPF
{
    /// <summary>
    /// Interaction logic for WinViewControl.xaml
    /// </summary>
    public partial class WinViewControl
    {
        #region Private Members

        /// <summary>
        /// 
        /// </summary>
        private int m_updateRate;

        /// <summary>
        /// 
        /// </summary>
        private IntPtr m_captureWindowHandle;

        /// <summary>
        /// The task used to call the real-time window capturing.
        /// </summary>
        private Task m_captureTask;

        /// <summary>
        /// The cancellation token for the capture task.
        /// </summary>
        private CancellationTokenSource m_cancellationTokenSource;

        #endregion

        #region Public Properties


        /// <summary>
        /// 
        /// </summary>
        public bool IsCapturing { get; private set; }

        #endregion

        #region Dependency Properties

        /// <summary>
        /// The update rate property.
        /// </summary>
        public static readonly DependencyProperty UpdateRateProperty =
            DependencyProperty.Register("UpdateRate", typeof(int), typeof(WinViewControl), 
                new FrameworkPropertyMetadata(1000 / 60, OnUpdateRatePropertyChanged));

        /// <summary>
        /// Updates the user interface for the rate property changed action.
        /// </summary>
        /// <param name="source">.</param>
        /// <param name="e">Event information to send to registered event handlers.</param>
        private static void OnUpdateRatePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            var control = source as WinViewControl;
            if (control != null)
            {
                control.m_updateRate = control.UpdateRate;
            }
        }

        /// <summary>
        /// Gets or sets the update rate.
        /// </summary>
        /// <value>
        /// The update rate.
        /// </value>
        public int UpdateRate
        {
            get { return 1000 / (int)GetValue(UpdateRateProperty); }
            set { SetValue(UpdateRateProperty, 1000 / value); }
        }

        /// <summary>
        /// The window handle property.
        /// </summary>
        public static readonly DependencyProperty WindowHandleProperty =
            DependencyProperty.Register("WindowHandle", typeof(IntPtr), typeof(WinViewControl),
                new FrameworkPropertyMetadata(User32.GetDesktopWindow(), OnWindowHandlePropertyChanged));

        /// <summary>
        /// Raises the dependency property changed event.
        /// </summary>
        /// <param name="source">.</param>
        /// <param name="e">Event information to send to registered event handlers.</param>
        private static void OnWindowHandlePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            var control = source as WinViewControl;
            if (control != null)
            {
                control.m_captureWindowHandle = control.WindowHandle == IntPtr.Zero
                    ? User32.GetDesktopWindow()
                    : control.WindowHandle;

                if (control.IsCapturing)
                {
                    // Restart the capture task
                    control.m_cancellationTokenSource.Cancel();
                }

                control.m_cancellationTokenSource = new CancellationTokenSource();
                control.m_captureTask = new Task(control.CaptureWindow);
                control.IsCapturing = true;
                control.m_captureTask.Start();
            }
        }

        /// <summary>
        /// Gets or sets the handle of the window.
        /// </summary>
        /// <value>
        /// The window handle.
        /// </value>
        public IntPtr WindowHandle
        {
            get { return (IntPtr)GetValue(WindowHandleProperty); }
            set { SetValue(WindowHandleProperty, value); }
        }

        /// <summary>
        /// The stretch property.
        /// </summary>
        public static readonly DependencyProperty StretchProperty =
            DependencyProperty.Register("Stretch", typeof(Stretch), typeof(WinViewControl),
                new FrameworkPropertyMetadata(Stretch.Uniform, OnStretchPropertyChanged));

        /// <summary>
        /// Raises the dependency property changed event.
        /// </summary>
        /// <param name="source">.</param>
        /// <param name="e">Event information to send to registered event handlers.</param>
        private static void OnStretchPropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            var control = source as WinViewControl;
            if (control != null)
            {
                control.RenderImage.Stretch = control.Stretch;
            }
        }

        /// <summary>
        /// Gets or sets the stretch.
        /// </summary>
        /// <value>
        /// The stretch.
        /// </value>
        public Stretch Stretch
        {
            get { return (Stretch)GetValue(StretchProperty); }
            set { SetValue(StretchProperty, value); }
        }

        #endregion

        /// <summary>
        /// Default constructor
        /// </summary>
        public WinViewControl()
        {
            InitializeComponent();
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Called when the control is unloaded.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="routedEventArgs"></param>
        private void OnUnloaded(object sender, RoutedEventArgs routedEventArgs)
        {
            if (IsCapturing)
            {
                m_cancellationTokenSource.Cancel();
                IsCapturing = false;
            }
        }

        private delegate void UpdateViewport(BitmapSource data);

        /// <summary>
        /// Called by the capture task.
        /// This is where the window is captured and copied to the render image.
        /// </summary>
        private void CaptureWindow()
        {
            var token = m_cancellationTokenSource.Token;
            var dispatcher = Application.Current.Dispatcher;

            var updateViewport = new UpdateViewport((data) =>
            {
                RenderImage.Source = data;
            });

            var captureHandle = m_captureWindowHandle;
            using (var captureWindow = new GdiWindowCapture(captureHandle))
            {
                while (!token.IsCancellationRequested)
                {
                    var startTime = DateTime.Now;

                    captureWindow.CaptureWindow(
                        windowImage =>
                        {
                            // Jump back to the application dispatcher to set the actual image source
                            dispatcher.BeginInvoke(DispatcherPriority.Render, updateViewport, windowImage);
                        }
                    );

                    var captureTime = DateTime.Now - startTime;
                    
                    // Offset wait time with the amount of time it took to capture.
                    // This means we are limited to the update rate, but if capturing
                    // takes longer than a 'single frame' we don't stall.
                    var wait = m_updateRate - captureTime.Milliseconds;
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

        internal interface IWindowCapture<out T>
        {
            void CaptureWindow(Action<T> callback);
        }

        /// <summary>
        /// Wrapper around GDI window capturing.
        /// </summary>
        internal class GdiWindowCapture : IWindowCapture<BitmapSource>, IDisposable
        {
            private readonly IntPtr m_windowHandle;
            private Win32Rect m_windowRect;

            private IntPtr m_sourceDc;
            private IntPtr m_targetDc;
            private IntPtr m_hbitmap;
            private IntPtr m_bitmapMemorySection;
            private int m_bitmapStride;
            private PixelFormat m_bitmapPixelFormat;
            
            /// <summary>
            /// Initialize all Gdi resources.
            /// </summary>
            /// <param name="windowHandle"></param>
            public GdiWindowCapture(IntPtr windowHandle)
            {
                m_windowHandle = windowHandle;
                CreateResources();
            }

            /// <summary>
            /// Destroy all Gdi resources.
            /// </summary>
            public void Dispose()
            {
                DestroyResources();
            }

            /// <summary>
            /// Creates the resources.
            /// </summary>
            private void CreateResources()
            {
                User32.GetClientRect(m_windowHandle, out m_windowRect);
                m_sourceDc = User32.GetDC(m_windowHandle);

                m_bitmapPixelFormat = PixelFormats.Bgr32;
                var bpp = m_bitmapPixelFormat.BitsPerPixel;
                m_bitmapStride = (m_windowRect.Width * (bpp / 8));
                var byteCount = (uint)(m_bitmapStride * m_windowRect.Height);

                m_bitmapMemorySection = Gdi32.CreateFileMapping(
                    Gdi32.INVALID_HANDLE_VALUE, IntPtr.Zero,
                    Gdi32.FileMapProtection.PageReadWrite, 0, byteCount, null);

                var bitmapInfo = new Gdi32.BITMAPINFO
                {
                    biSize = 40,
                    biWidth = m_windowRect.Width,
                    biHeight = -m_windowRect.Height,
                    biPlanes = 1,
                    biBitCount = (short)bpp,
                    biCompression = (uint)Gdi32.BitmapCompressionMode.BI_RGB
                };

                IntPtr bitmapBits;
                m_hbitmap = Gdi32.CreateDIBSection(
                    m_sourceDc, ref bitmapInfo, 
                    Gdi32.DIB_Color_Mode.DIB_RGB_COLORS,
                    out bitmapBits,
                    m_bitmapMemorySection, 0);

                m_targetDc = Gdi32.CreateCompatibleDC(m_sourceDc);
            }

            /// <summary>
            /// Destroys the resources.
            /// </summary>
            private void DestroyResources()
            {
                User32.ReleaseDC(IntPtr.Zero, m_sourceDc);
                Gdi32.DeleteObject(m_hbitmap);
                Gdi32.CloseHandle(m_bitmapMemorySection);
                User32.ReleaseDC(IntPtr.Zero, m_targetDc);
            }

            /// <summary>
            /// Copy the target windows contents to a bitmap handle.
            /// Then call the callback with the address of the handle.
            /// </summary>
            /// <param name="callback"></param>
            public void CaptureWindow(Action<BitmapSource> callback)
            {
                try
                {
                    // Check to see if the window size has changed.
                    Win32Rect rect;
                    if (User32.GetClientRect(m_windowHandle, out rect) && 
                        (rect.Width != m_windowRect.Width || rect.Height != m_windowRect.Height))
                    {
                        DestroyResources();
                        CreateResources();
                    }

                    // Check for invalid window size.
                    if (m_windowRect.Width < 1 || m_windowRect.Height < 1)
                    {
                        return;
                    }

                    // Make our render bitmap active
                    var oldDc = Gdi32.SelectObject(m_targetDc, m_hbitmap);

                    // BitBlt to our render bitmap
                    if (m_bitmapMemorySection != IntPtr.Zero &&
                        Gdi32.BitBlt(m_targetDc, 0, 0, m_windowRect.Width, m_windowRect.Height,
                        m_sourceDc, 0, 0, Gdi32.Srccopy))
                    {
                        // Create a wpf bitmap from the render bitmaps memory section
                        var bitmap = Imaging.CreateBitmapSourceFromMemorySection(
                            m_bitmapMemorySection,
                            m_windowRect.Width, m_windowRect.Height,
                            m_bitmapPixelFormat, m_bitmapStride, 0);
                        bitmap.Freeze();
                        callback(bitmap);
                    }

                    // Reset the active object in the render target dc.
                    Gdi32.SelectObject(m_targetDc, oldDc);
                }
                catch(Exception)
                {
                    // Ignored
                }
            }
        }
    }
}

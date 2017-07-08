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

        /// <summary>
        /// 
        /// </summary>
        public bool IsCapturing { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public static readonly DependencyProperty UpdateRateProperty =
            DependencyProperty.Register("UpdateRate", typeof(int), typeof(WinViewControl), 
                new FrameworkPropertyMetadata(1000 / 60, OnUpdateRatePropertyChanged));

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private static void OnUpdateRatePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            var control = source as WinViewControl;
            if (control != null)
            {
                control.m_updateRate = control.UpdateRate;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public int UpdateRate
        {
            get { return 1000 / (int)GetValue(UpdateRateProperty); }
            set { SetValue(UpdateRateProperty, 1000 / value); }
        }

        public static readonly DependencyProperty WindowHandleProperty =
            DependencyProperty.Register("WindowHandle", typeof(IntPtr), typeof(WinViewControl),
                new FrameworkPropertyMetadata(User32.GetDesktopWindow(), OnWindowHandlePropertyChanged));

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
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
                    control.m_cancellationTokenSource = new CancellationTokenSource();
                    control.m_captureTask = new Task(control.CaptureWindow);
                    control.m_captureTask.Start();
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public IntPtr WindowHandle
        {
            get { return (IntPtr)GetValue(WindowHandleProperty); }
            set { SetValue(WindowHandleProperty, value); }
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        public WinViewControl()
        {
            InitializeComponent();

            m_cancellationTokenSource = new CancellationTokenSource();
            m_captureTask = new Task(CaptureWindow);
            m_captureTask.Start();
            IsCapturing = true;

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

        /// <summary>
        /// Called by the capture task.
        /// This is where the window is captured and copied to the render image.
        /// </summary>
        private void CaptureWindow()
        {
            var token = m_cancellationTokenSource.Token;
            var dispatcher = Application.Current.Dispatcher;
            var bitmapSizeOptions = BitmapSizeOptions.FromEmptyOptions();

            var captureHandle = m_captureWindowHandle;
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

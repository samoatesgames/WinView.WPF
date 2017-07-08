using System;
using System.Threading;
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
        private bool m_capturing = false;

        /// <summary>
        /// 
        /// </summary>
        public WinViewControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="routedEventArgs"></param>
        private void OnUnloaded(object sender, RoutedEventArgs routedEventArgs)
        {
            m_capturing = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="routedEventArgs"></param>
        private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            const int updateRate = 1000 / 60;

            var dispatcher = Application.Current.Dispatcher;
            var bitmapSizeOptions = BitmapSizeOptions.FromEmptyOptions();

            ThreadPool.QueueUserWorkItem(x =>
            {
                m_capturing = true;
                using (var captureWindow = new WindowCapture(User32.GetDesktopWindow()))
                {
                    while (m_capturing)
                    {
                        captureWindow.CaptureWindow(
                            dc =>
                            {
                                dispatcher.Invoke(() =>
                                {
                                    Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                        dc, IntPtr.Zero, Int32Rect.Empty, bitmapSizeOptions
                                        );
                                }, DispatcherPriority.Render);
                            }
                        );

                        //var wait = updateRate;
                        //while ((--wait) >= 0)
                        //{
                        //    Thread.Sleep(1);
                        //    if (!m_capturing)
                        //    {
                        //        return;
                        //    }
                        //}
                    }
                }
            });
        }

        internal class WindowCapture : IDisposable
        {
            private readonly Win32Rect m_windowRect;
            private readonly IntPtr m_sourceDc;
            private readonly IntPtr m_targetDc;
            private readonly IntPtr m_compatibleBitmapHandle;

            /// <summary>
            /// 
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
            /// 
            /// </summary>
            public void Dispose()
            {
                Gdi32.DeleteObject(m_compatibleBitmapHandle);
                User32.ReleaseDC(IntPtr.Zero, m_sourceDc);
                User32.ReleaseDC(IntPtr.Zero, m_targetDc);
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="callback"></param>
            public void CaptureWindow(Action<IntPtr> callback)
            {
                try
                {
                    if (Gdi32.BitBlt(m_targetDc, 0, 0, m_windowRect.Width, m_windowRect.Height, m_sourceDc, 0, 0,
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

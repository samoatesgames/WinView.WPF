using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using WinView.WPF.Example.Annotations;

namespace WinView.WPF.Example
{
    public class WindowViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// 
        /// </summary>
        private IntPtr m_captureWindow;

        /// <summary>
        /// 
        /// </summary>
        private string m_selectedWindowName;

        /// <summary>
        /// 
        /// </summary>
        private readonly Dictionary<string, Process> m_processNameProcess = new Dictionary<string, Process>();

        /// <summary>
        /// 
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="propertyName"></param>
        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// 
        /// </summary>
        public IntPtr CaptureWindow
        {
            get { return m_captureWindow; }
            set
            {
                m_captureWindow = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public ObservableCollection<string> WindowNames { get; } = new ObservableCollection<string>();

        /// <summary>
        /// 
        /// </summary>
        public string SelectedWindowName
        {
            get { return m_selectedWindowName; }
            set
            {
                m_selectedWindowName = value;
                OnPropertyChanged();

                CaptureWindow = m_processNameProcess[value].MainWindowHandle;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public WindowViewModel()
        {
            CaptureWindow = IntPtr.Zero;

            foreach (var process in Process.GetProcesses().Where(x => x.MainWindowHandle != IntPtr.Zero))
            {
                WindowNames.Add(process.ProcessName);
                m_processNameProcess[process.ProcessName] = process;
            }
        }
    }
}

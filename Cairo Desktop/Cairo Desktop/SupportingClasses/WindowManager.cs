﻿using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using CairoDesktop.Common.Logging;
using CairoDesktop.Interop;

namespace CairoDesktop.SupportingClasses
{
    public sealed class WindowManager : IDisposable
    {
        private bool hasCompletedInitialDisplaySetup;
        private int pendingDisplayEvents;
        private readonly static object displaySetupLock = new object();
        private readonly IEnumerable<IWindowService> _windowServices;

        public bool IsSettingDisplays { get; set; }
        public Screen[] ScreenState = Array.Empty<Screen>();

        public EventHandler<WindowManagerEventArgs> DwmChanged;
        public EventHandler<WindowManagerEventArgs> ScreensChanged;

        public static System.Drawing.Size PrimaryMonitorSize
        {
            get
            {
                return new System.Drawing.Size(Convert.ToInt32(SystemParameters.PrimaryScreenWidth / Shell.DpiScaleAdjustment), Convert.ToInt32(SystemParameters.PrimaryScreenHeight / Shell.DpiScaleAdjustment));
            }
        }

        public static System.Drawing.Size PrimaryMonitorDeviceSize
        {
            get
            {
                return new System.Drawing.Size(NativeMethods.GetSystemMetrics(0), NativeMethods.GetSystemMetrics(1));
            }
        }

        public static System.Drawing.Size PrimaryMonitorWorkArea
        {
            get
            {
                return new System.Drawing.Size(SystemInformation.WorkingArea.Right - SystemInformation.WorkingArea.Left, SystemInformation.WorkingArea.Bottom - SystemInformation.WorkingArea.Top);
            }
        }

        public WindowManager(IEnumerable<IWindowService> windowServices, DesktopManager desktopManager)
        {
            _windowServices = windowServices;

            desktopManager.Initialize(this);

            // start a timer to handle orphaned display events
            DispatcherTimer notificationCheckTimer = new DispatcherTimer();

            notificationCheckTimer = new DispatcherTimer(DispatcherPriority.Background, System.Windows.Application.Current.Dispatcher)
            {
                Interval = new TimeSpan(0, 0, 0, 0, 100)
            };

            notificationCheckTimer.Tick += NotificationCheck_Tick;
            notificationCheckTimer.Start();
        }

        private void NotificationCheck_Tick(object sender, EventArgs e)
        {
            if (!IsSettingDisplays && pendingDisplayEvents > 0)
            {
                CairoLogger.Instance.Debug("WindowManager: Processing additional display events");
                ProcessDisplayChanges(ScreenSetupReason.Reconciliation);
            }
        }

        public void InitialSetup()
        {
            IsSettingDisplays = true;

            foreach (var windowService in _windowServices)
            {
                windowService.Initialize(this);
            }

            DisplaySetup(ScreenSetupReason.FirstRun);

            hasCompletedInitialDisplaySetup = true;
            IsSettingDisplays = false;
        }

        public void NotifyDisplayChange(ScreenSetupReason reason)
        {
            CairoLogger.Instance.Debug($"WindowManager: Received {reason} notification");

            if (reason == ScreenSetupReason.DwmChange)
            {
                lock (displaySetupLock)
                {
                    DwmChanged?.Invoke(this, new WindowManagerEventArgs());
                }
            }
            else
            {
                pendingDisplayEvents++;

                if (!IsSettingDisplays)
                {
                    ProcessDisplayChanges(reason);
                }
            }
        }

        private bool HaveDisplaysChanged()
        {
            ResetScreenCache();

            if (ScreenState.Length == Screen.AllScreens.Length)
            {
                bool same = true;
                for (int i = 0; i < ScreenState.Length; i++)
                {
                    Screen current = Screen.AllScreens[i];
                    if (!(ScreenState[i].Bounds == current.Bounds && ScreenState[i].DeviceName == current.DeviceName && ScreenState[i].Primary == current.Primary && ScreenState[i].WorkingArea == current.WorkingArea))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    CairoLogger.Instance.Debug("WindowManager: No display changes");
                    return false;
                }
            }

            return true;
        }

        private void ProcessDisplayChanges(ScreenSetupReason reason)
        {
            lock (displaySetupLock)
            {
                IsSettingDisplays = true;

                while (pendingDisplayEvents > 0)
                {
                    if (HaveDisplaysChanged())
                    {
                        DisplaySetup(reason);
                    }
                    else
                    {
                        // if this is only a DPI change, screens will be the same but we still need to reposition
                        RefreshWindows(reason, false);
                        SetDisplayWorkAreas();
                    }

                    pendingDisplayEvents--;
                }

                IsSettingDisplays = false;
            }
        }

        private void ResetScreenCache()
        {
            // use reflection to empty screens cache
            typeof(Screen).GetField("screens", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, null);
        }

        /// <summary>
        /// Compares the system screen list to the screens associated with Cairo windows, then creates or destroys windows as necessary.
        /// Runs at startup and when a WM_DEVICECHANGE, WM_DISPLAYCHANGE, or WM_DPICHANGED message is received by the MenuBar window on the primary display.
        /// </summary>
        private void DisplaySetup(ScreenSetupReason reason)
        {
            if (reason != ScreenSetupReason.FirstRun && !hasCompletedInitialDisplaySetup)
            {
                CairoLogger.Instance.Debug("WindowManager: Display setup ran before startup completed, aborting");
                return;
            }

            CairoLogger.Instance.Debug("WindowManager: Beginning display setup");

            List<string> sysScreens = new List<string>();
            List<string> openScreens = new List<string>();
            List<string> addedScreens = new List<string>();
            List<string> removedScreens = new List<string>();

            // update our knowledge of the displays present
            ScreenState = Screen.AllScreens;

            // enumerate screens based on currently open windows
            openScreens = GetOpenScreens();

            sysScreens = GetScreenDeviceNames();

            // figure out which screens have been added
            foreach (string name in sysScreens)
            {
                if (!openScreens.Contains(name))
                {
                    addedScreens.Add(name);
                }
            }

            if (reason != ScreenSetupReason.FirstRun)
            {
                // figure out which screens have been removed
                foreach (string name in openScreens)
                {
                    if (!sysScreens.Contains(name))
                    {
                        removedScreens.Add(name);
                    }
                }

                // abort if we have no screens or if we are removing all screens without adding new ones
                if (sysScreens.Count == 0 || (removedScreens.Count >= openScreens.Count && addedScreens.Count == 0))
                {
                    CairoLogger.Instance.Debug("WindowManager: Aborted due to no displays present");
                    return;
                }

                // close windows associated with removed screens
                ProcessRemovedScreens(removedScreens);

                // refresh existing window screen properties with updated screen information
                RefreshWindows(reason, true);
            }

            // open windows on newly added screens
            ProcessAddedScreens(addedScreens);

            // update each display's work area if we are shell
            SetDisplayWorkAreas();

            CairoLogger.Instance.Debug("WindowManager: Completed display setup");
        }

        #region Display setup helpers
        private List<string> GetOpenScreens()
        {
            List<string> openScreens = new List<string>();

            foreach (var windowService in _windowServices)
            {
                foreach (var window in windowService.Windows)
                {
                    if (window.Screen != null && !openScreens.Contains(window.Screen.DeviceName))
                    {
                        openScreens.Add(window.Screen.DeviceName);
                    }
                }
            }

            return openScreens;
        }

        private List<string> GetScreenDeviceNames()
        {
            List<string> sysScreens = new List<string>();

            foreach (var screen in ScreenState)
            {
                CairoLogger.Instance.Debug(string.Format("WindowManager: {0} found at {1} with area {2}; primary? {3}", screen.DeviceName, screen.Bounds.ToString(), screen.WorkingArea.ToString(), screen.Primary.ToString()));
                sysScreens.Add(screen.DeviceName);
            }

            return sysScreens;
        }

        private void ProcessRemovedScreens(List<string> removedScreens)
        {
            foreach (string name in removedScreens)
            {
                CairoLogger.Instance.Debug("WindowManager: Removing windows associated with screen " + name);

                foreach (var windowService in _windowServices)
                {
                    windowService.HandleScreenRemoved(name);
                }
            }
        }

        private void RefreshWindows(ScreenSetupReason reason, bool displaysChanged)
        {
            CairoLogger.Instance.Debug("WindowManager: Refreshing screen information for existing windows");

            WindowManagerEventArgs args = new WindowManagerEventArgs { DisplaysChanged = displaysChanged, Reason = reason };

            foreach (var windowService in _windowServices)
            {
                windowService.RefreshWindows(args);
            }

            ScreensChanged?.Invoke(this, args);
        }

        private void ProcessAddedScreens(List<string> addedScreens)
        {
            foreach (var screen in ScreenState)
            {
                if (addedScreens.Contains(screen.DeviceName))
                {
                    CairoLogger.Instance.Debug("WindowManager: Opening windows on screen " + screen.DeviceName);

                    foreach (var windowService in _windowServices)
                    {
                        windowService.HandleScreenAdded(screen);
                    }
                }
            }
        }

        public static T GetScreenWindow<T>(List<T> windowList, Screen screen) where T : AppBarWindow
        {
            foreach (AppBarWindow window in windowList)
            {
                if (window.Screen?.DeviceName == screen.DeviceName)
                {
                    return (T)window;
                }
            }

            return null;
        }
        #endregion

        #region Work area
        private void SetDisplayWorkAreas()
        {
            // Set desktop work area for when Explorer isn't running
            if (Shell.IsCairoRunningAsShell)
            {
                foreach (var screen in ScreenState)
                {
                    SetWorkArea(screen);
                }
            }
        }

        public NativeMethods.Rect GetWorkArea(ref double dpiScale, Screen screen, bool edgeBarsOnly, bool enabledBarsOnly)
        {
            double topEdgeWindowHeight = 0;
            double bottomEdgeWindowHeight = 0;
            NativeMethods.Rect rc;
            rc.Left = screen.Bounds.Left;
            rc.Right = screen.Bounds.Right;

            // get appropriate windows for this display
            foreach (var windowService in _windowServices)
            {
                foreach (var window in windowService.Windows)
                {
                    if (window.Screen.DeviceName == screen.DeviceName)
                    {
                        if ((window.enableAppBar || !enabledBarsOnly) && (window.requiresScreenEdge || !edgeBarsOnly))
                        {
                            if (window.appBarEdge == NativeMethods.ABEdge.ABE_TOP)
                            {
                                topEdgeWindowHeight += window.ActualHeight;
                            }
                            else if (window.appBarEdge == NativeMethods.ABEdge.ABE_BOTTOM)
                            {
                                bottomEdgeWindowHeight += window.ActualHeight;
                            }
                        }

                        dpiScale = window.dpiScale;
                        break;
                    }
                }
            }

            rc.Top = screen.Bounds.Top + (int)(topEdgeWindowHeight * dpiScale);
            rc.Bottom = screen.Bounds.Bottom - (int)(bottomEdgeWindowHeight * dpiScale);

            return rc;
        }

        public void SetWorkArea(Screen screen)
        {
            double dpiScale = 1;
            NativeMethods.Rect rc = GetWorkArea(ref dpiScale, screen, false, true);

            NativeMethods.SystemParametersInfo((int)NativeMethods.SPI.SETWORKAREA, 1, ref rc, (uint)(NativeMethods.SPIF.UPDATEINIFILE | NativeMethods.SPIF.SENDWININICHANGE));
        }

        public static void ResetWorkArea()
        {
            if (Shell.IsCairoRunningAsShell)
            {
                // TODO this is wrong for multi-display
                // set work area back to full screen size. we can't assume what pieces of the old work area may or may not be still used
                NativeMethods.Rect oldWorkArea;
                oldWorkArea.Left = SystemInformation.VirtualScreen.Left;
                oldWorkArea.Top = SystemInformation.VirtualScreen.Top;
                oldWorkArea.Right = SystemInformation.VirtualScreen.Right;
                oldWorkArea.Bottom = SystemInformation.VirtualScreen.Bottom;

                NativeMethods.SystemParametersInfo((int)NativeMethods.SPI.SETWORKAREA, 1, ref oldWorkArea,
                    (uint)(NativeMethods.SPIF.UPDATEINIFILE | NativeMethods.SPIF.SENDWININICHANGE));
            }
        }
        #endregion

        public void Dispose()
        {
        }
    }
}
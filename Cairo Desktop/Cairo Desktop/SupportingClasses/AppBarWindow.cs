﻿using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CairoDesktop.Common.Logging;
using CairoDesktop.Configuration;
using CairoDesktop.Interop;

namespace CairoDesktop.SupportingClasses
{
    public class AppBarWindow : Window
    {
        protected readonly WindowManager _windowManager;
        public System.Windows.Forms.Screen Screen;
        public double dpiScale = 1.0;
        protected bool processScreenChanges;

        // Window properties
        private WindowInteropHelper helper;
        private bool isRaising;
        public IntPtr Handle;
        public bool IsClosing;
        protected double desiredHeight;
        private bool enableBlur;

        // AppBar properties
        private int appbarMessageId = -1;
        public NativeMethods.ABEdge appBarEdge = NativeMethods.ABEdge.ABE_TOP;
        internal bool enableAppBar = true;
        internal bool requiresScreenEdge;

        public AppBarWindow() : this(null)
        { }

        public AppBarWindow(WindowManager windowManager)
        {
            _windowManager = windowManager;

            Closing += OnClosing;
            SourceInitialized += OnSourceInitialized;

            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Title = "";
            Topmost = true;
            UseLayoutRounding = true;
            WindowStyle = WindowStyle.None;
        }

        #region Events
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            // set up helper and get handle
            helper = new WindowInteropHelper(this);
            Handle = helper.Handle;

            // set up window procedure
            HwndSource source = HwndSource.FromHwnd(Handle);
            source.AddHook(WndProc);

            // set initial DPI. We do it here so that we get the correct value when DPI has changed since initial user logon to the system.
            if (Screen.Primary)
            {
                Shell.DpiScale = PresentationSource.FromVisual(CairoApplication.Current.MainWindow).CompositionTarget.TransformToDevice.M11;
            }

            dpiScale = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;

            SetPosition();

            if (Shell.IsCairoRunningAsShell)
            {
                // set position again, on a delay, in case one display has a different DPI. for some reason the system overrides us if we don't wait
                DelaySetPosition();
            }

            RegisterAppBar();

            // hide from alt-tab etc
            Shell.HideWindowFromTasks(Handle);

            // register for full-screen notifications
            FullScreenHelper.Instance.FullScreenApps.CollectionChanged += FullScreenApps_CollectionChanged;

            PostInit();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            IsClosing = true;

            CustomClosing();

            if (_windowManager.IsSettingDisplays || CairoApplication.IsShuttingDown)
            {
                UnregisterAppBar();

                // unregister full-screen notifications
                FullScreenHelper.Instance.FullScreenApps.CollectionChanged -= FullScreenApps_CollectionChanged;
            }
            else
            {
                IsClosing = false;
                e.Cancel = true;
            }
        }

        private void FullScreenApps_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            bool found = false;

            foreach (FullScreenApp app in FullScreenHelper.Instance.FullScreenApps)
            {
                if (app.screen.DeviceName == Screen.DeviceName)
                {
                    // we need to not be on top now
                    found = true;
                    break;
                }
            }

            if (found && Topmost)
            {
                setFullScreenMode(true);
            }
            else if (!found && !Topmost)
            {
                setFullScreenMode(false);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == appbarMessageId && appbarMessageId != -1)
            {
                switch ((NativeMethods.AppBarNotifications)wParam.ToInt32())
                {
                    case NativeMethods.AppBarNotifications.PosChanged:
                        AppBarHelper.ABSetPos(this, ActualWidth * dpiScale, desiredHeight * dpiScale, appBarEdge);
                        break;

                    case NativeMethods.AppBarNotifications.WindowArrange:
                        if ((int)lParam != 0) // before
                        {
                            Visibility = Visibility.Collapsed;
                        }
                        else // after
                        {
                            Visibility = Visibility.Visible;
                        }

                        break;

                    case NativeMethods.AppBarNotifications.FullScreenApp:
                        AppBarHelper.SetWinTaskbarVisibility((int)NativeMethods.SetWindowPosFlags.SWP_HIDEWINDOW);
                        break;
                }
                handled = true;
            }
            else if (msg == (int)NativeMethods.WM.ACTIVATE && enableAppBar && !Shell.IsCairoRunningAsShell && !CairoApplication.IsShuttingDown)
            {
                AppBarHelper.AppBarActivate(hwnd);
            }
            else if (msg == (int)NativeMethods.WM.WINDOWPOSCHANGING)
            {
                // Extract the WINDOWPOS structure corresponding to this message
                NativeMethods.WINDOWPOS wndPos = NativeMethods.WINDOWPOS.FromMessage(lParam);

                // Determine if the z-order is changing (absence of SWP_NOZORDER flag)
                // If we are intentionally trying to become topmost, make it so
                if (isRaising && (wndPos.flags & NativeMethods.SetWindowPosFlags.SWP_NOZORDER) == 0)
                {
                    // Sometimes Windows thinks we shouldn't go topmost, so poke here to make it happen.
                    wndPos.hwndInsertAfter = (IntPtr)NativeMethods.WindowZOrder.HWND_TOPMOST;
                    wndPos.UpdateMessage(lParam);
                }
            }
            else if (msg == (int)NativeMethods.WM.WINDOWPOSCHANGED && enableAppBar && !Shell.IsCairoRunningAsShell && !CairoApplication.IsShuttingDown)
            {
                AppBarHelper.AppBarWindowPosChanged(hwnd);
            }
            else if (msg == (int)NativeMethods.WM.DPICHANGED)
            {
                if (Screen.Primary)
                {
                    Shell.DpiScale = (wParam.ToInt32() & 0xFFFF) / 96d;
                }

                dpiScale = (wParam.ToInt32() & 0xFFFF) / 96d;

                SetScreenProperties(ScreenSetupReason.DpiChange);
            }
            else if (msg == (int)NativeMethods.WM.DISPLAYCHANGE)
            {
                SetScreenProperties(ScreenSetupReason.DisplayChange);
                handled = true;
            }
            else if (msg == (int)NativeMethods.WM.DEVICECHANGE && (int)wParam == 0x0007)
            {
                SetScreenProperties(ScreenSetupReason.DeviceChange);
                handled = true;
            }
            else if (msg == (int)NativeMethods.WM.DWMCOMPOSITIONCHANGED)
            {
                SetScreenProperties(ScreenSetupReason.DwmChange);
                handled = true;
            }

            // call custom implementations' window procedure
            return CustomWndProc(hwnd, msg, wParam, lParam, ref handled);
        }
        #endregion

        #region Helpers
        private void DelaySetPosition()
        {
            // delay changing things when we are shell. it seems that explorer AppBars do this too.
            // if we don't, the system moves things to bad places
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.1) };
            timer.Start();
            timer.Tick += (sender1, args) =>
            {
                SetPosition();
                timer.Stop();
            };
        }

        internal void SetScreenPosition()
        {
            // set our position if running as shell, otherwise let AppBar do the work
            if (Shell.IsCairoRunningAsShell || !enableAppBar)
            {
                DelaySetPosition();
            }
            else if (enableAppBar)
            {
                AppBarHelper.ABSetPos(this, ActualWidth * dpiScale, desiredHeight * dpiScale, appBarEdge);
            }
        }

        internal void SetAppBarPosition(NativeMethods.Rect rect)
        {
            Top = rect.Top / dpiScale;
            Left = rect.Left / dpiScale;
            Width = (rect.Right - rect.Left) / dpiScale;
            Height = (rect.Bottom - rect.Top) / dpiScale;
        }

        private void SetScreenProperties(ScreenSetupReason reason)
        {
            // process screen changes if we are on the primary display and the designated window
            // (or any display in the case of a DPI change, since only the changed display receives that message and not all windows receive it reliably)
            // suppress this if we are shutting down (which can trigger this method on multi-dpi setups due to window movements)
            if (((Screen.Primary && processScreenChanges) || reason == ScreenSetupReason.DpiChange) && !CairoApplication.IsShuttingDown)
            {
                _windowManager.NotifyDisplayChange(reason); // update Cairo window list based on new screen setup
            }
        }

        private void setFullScreenMode(bool entering)
        {
            if (entering)
            {
                CairoLogger.Instance.Debug($"AppBarWindow: {Name} on {Screen.DeviceName} conceding to full-screen app");

                Topmost = false;
                Shell.ShowWindowBottomMost(Handle);
            }
            else
            {
                CairoLogger.Instance.Debug($"AppBarWindow: {Name} on {Screen.DeviceName} returning to normal state");

                isRaising = true;
                Topmost = true;
                Shell.ShowWindowTopMost(Handle);
                isRaising = false;
            }
        }

        protected void SetBlur(bool enable)
        {
            if (enableBlur != enable && Handle != IntPtr.Zero)
            {
                enableBlur = enable;
                Shell.SetWindowBlur(Handle, enable);
            }
        }

        protected void RegisterAppBar()
        {
            if (!Shell.IsCairoRunningAsShell && enableAppBar && !AppBarHelper.appBars.Contains(Handle))
            {
                appbarMessageId = AppBarHelper.RegisterBar(this, ActualWidth * dpiScale, desiredHeight * dpiScale, appBarEdge);
            }
        }

        protected void UnregisterAppBar()
        {
            if (AppBarHelper.appBars.Contains(Handle))
            {
                AppBarHelper.RegisterBar(this, ActualWidth * dpiScale, desiredHeight * dpiScale);
            }
        }
        #endregion

        #region Virtual methods
        internal virtual void AfterAppBarPos(bool isSameCoords, NativeMethods.Rect rect)
        {
            // apparently the TaskBars like to pop up when AppBars change
            if (Settings.Instance.EnableTaskbar && !CairoApplication.IsShuttingDown)
            {
                AppBarHelper.HideWindowsTaskbar();
            }

            if (!isSameCoords)
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.1) };
                timer.Start();
                timer.Tick += (sender1, args) =>
                {
                    // set position again, since WPF may have overridden the original change from AppBarHelper
                    SetAppBarPosition(rect);

                    timer.Stop();
                };
            }
        }

        protected virtual void PostInit() { }

        protected virtual void CustomClosing() { }

        protected virtual IntPtr CustomWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            return IntPtr.Zero;
        }

        internal virtual void SetPosition() { }
        #endregion
    }
}
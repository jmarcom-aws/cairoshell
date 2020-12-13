using CairoDesktop.Application.Interfaces;
using CairoDesktop.Common;
using CairoDesktop.Configuration;
using CairoDesktop.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.Devices;
using System;
using System.Diagnostics;

namespace CairoDesktop
{
    public partial class CairoApplication
    {
        private void ProcessCommandLineArgs(string[] args)
        {
            _commandLineParser = new CommandLineParser(args);

            _isRestart = _commandLineParser.ToBoolean("restart");
            _isTour = _commandLineParser.ToBoolean("tour");
            _forceEnableShellMode = _commandLineParser.ToBoolean("shell");
            _forceDisableShellMode = _commandLineParser.ToBoolean("noshell");
        }

        public void SetIsCairoRunningAsShell()
        {
            // check if there is an existing shell window. If not, we will assume the role of shell.
            Shell.IsCairoRunningAsShell = (NativeMethods.GetShellWindow() == IntPtr.Zero && !_forceDisableShellMode) || _forceEnableShellMode;
        }

        private void SetupUpdateManager()
        {
            var service = Host.Services.GetService<IApplicationUpdateService>();
            service?.Initialize(ExitCairo);
        }

        private bool SingleInstanceCheck()
        {
            _cairoMutex = new System.Threading.Mutex(true, "CairoShell", out bool ok);

            if (!ok && !_isRestart)
            {
                // Another instance is already running.
                return false;
            }
            else if (!ok && _isRestart)
            {
                // this is a restart so let's wait for the old instance to end
                System.Threading.Thread.Sleep(2000);
            }

            return true;
        }

        private void SetShellReadyEvent()
        {
            int hShellReadyEvent;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && Shell.IsWindows2kOrBetter)
            {
                hShellReadyEvent = NativeMethods.OpenEvent(NativeMethods.EVENT_MODIFY_STATE, true, @"Global\msgina: ShellReadyEvent");
            }
            else
            {
                hShellReadyEvent = NativeMethods.OpenEvent(NativeMethods.EVENT_MODIFY_STATE, false, "msgina: ShellReadyEvent");
            }

            if (hShellReadyEvent != 0)
            {
                NativeMethods.SetEvent(hShellReadyEvent);
                NativeMethods.CloseHandle(hShellReadyEvent);
            }
        }

        private void SetupSettings()
        {
            if (Settings.Instance.IsFirstRun)
            {
                Settings.Instance.Upgrade();
            }
        }

        internal void LoadExtensions()
        {
            var pluginService = Host.Services.GetService<IExtensionService>();
            pluginService?.Start();
        }
        internal void WriteApplicationDebugInfoToConsole(ILogger logger)
        {
            const string @break = @"#############################################";

            logger.LogInformation(@break);
            logger.LogInformation($"{CairoApplication.ProductName}");
            logger.LogInformation($"Version: {CairoApplication.ProductVersion}");
            logger.LogInformation($"Operating System: {new ComputerInfo().OSFullName}");
            logger.LogInformation($"OS Build: {new ComputerInfo().OSVersion}");
            logger.LogInformation($"Processor Type: {(IntPtr.Size == 8 || InternalCheckIsWow64() ? 64 : 32)}-bit");
            logger.LogInformation($"Startup Path: {CairoApplication.StartupPath}");
            logger.LogInformation($"Running As: {IntPtr.Size * 8}-bit Process");
            logger.LogInformation($"Configured as shell: {Shell.IsCairoConfiguredAsShell}");
            logger.LogInformation($"Running as shell: {Shell.IsCairoRunningAsShell}");
            logger.LogInformation(@break);
        }

        internal bool InternalCheckIsWow64()
        {
            if ((Environment.OSVersion.Version.Major == 5 && Environment.OSVersion.Version.Minor >= 1) || Environment.OSVersion.Version.Major >= 6)
            {
                using (Process p = Process.GetCurrentProcess())
                {
                    bool retVal;

                    try
                    {
                        if (!NativeMethods.IsWow64Process(p.Handle, out retVal))
                        {
                            return false;
                        }
                    }
                    catch (Exception)
                    {
                        return false;
                    }

                    return retVal;
                }
            }
            return false;
        }

        internal void SetSystemKeyboardShortcuts()
        {
            if (Shell.IsCairoRunningAsShell)
            {
                // Commenting out as per comments on PR #274
                SupportingClasses.SystemHotKeys.RegisterSystemHotkeys();
            }
        }
    }
}
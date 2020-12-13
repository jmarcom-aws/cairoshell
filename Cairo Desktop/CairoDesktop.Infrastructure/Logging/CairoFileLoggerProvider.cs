using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.IO;

namespace CairoDesktop.Infrastructure.Logging
{
    [ProviderAlias("CairoFile")]
    public class CairoFileLoggerProvider : ILoggerProvider
    {
        public readonly CairoFileLoggerOptions Options;
        private readonly LegacyCairoLogObserver legacyCairoLogObserver;

        public CairoFileLoggerProvider(IOptions<CairoFileLoggerOptions> _options)
        {
            Options = _options.Value;

            PerformDirectorySetupAndMaintenance();
        }

        private void PerformDirectorySetupAndMaintenance()
        {
            string logsFolder = Options.FolderPath;

            if (!CreateLogsFolder(logsFolder))
            {
                BackupExistingLogFiles(logsFolder);
                DeleteOldLogFiles(logsFolder);
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new CairoFileLogger(this, categoryName);
        }

        public void Dispose()
        {
        }

        public string LogFile
        {
            get
            {
                return Path.Combine(Options.FolderPath, LogName);
            }
        }

        #region Log File Management

        private string DateFormat
        {
            get
            {
                return "MM-dd-yyyy";
            }
        }

        private string LogFileExtension
        {
            get
            {
                return "log";
            }
        }

        private string LogName
        {
            get
            {
                return string.Format("{0}.{1}", DateTime.Now.ToString(DateFormat), LogFileExtension);
            }
        }

        private string BackupLogName
        {
            get
            {
                return string.Format("{0}-Backup.{1}", DateTime.Now.ToString(DateFormat), LogFileExtension);
            }
        }

        /// <summary>
        /// Creates the logs folder. Returns true if the directory was created, false otherwise.
        /// </summary>
        /// <param name="logsFolder">The directory to create.</param>
        /// <returns></returns>
        private bool CreateLogsFolder(string logsFolder)
        {
            try
            {
                if (Directory.Exists(logsFolder))
                {
                    return false;
                }

                Directory.CreateDirectory(logsFolder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            return true;
        }

        /// <summary>
        /// Backs up the existing log file, as the last run backup.
        /// </summary>
        /// <param name="logsFolder"></param>
        private void BackupExistingLogFiles(string logsFolder)
        {
            try
            {
                string currentFilename = Path.Combine(logsFolder, LogName);
                if (File.Exists(currentFilename))
                {
                    string backupFilename = Path.Combine(logsFolder, BackupLogName);
                    if (File.Exists(backupFilename))
                    {
                        File.Delete(backupFilename);
                    }

                    File.Move(currentFilename, backupFilename);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// Deletes any log files older than a week.
        /// </summary>
        /// <param name="logsFolder"></param>
        private void DeleteOldLogFiles(string logsFolder)
        {
            try
            {
                // look for all of the log files
                DirectoryInfo info = new DirectoryInfo(logsFolder);
                FileInfo[] files = info.GetFiles(string.Format("*.{0}", LogFileExtension), SearchOption.TopDirectoryOnly);

                // delete any files that are more than a week old
                DateTime now = DateTime.Now;
                TimeSpan allowedDelta = new TimeSpan(7, 0, 0);

                foreach (FileInfo file in files)
                {
                    if (now.Subtract(file.LastWriteTime) > allowedDelta)
                    {
                        file.Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        #endregion
    }
}
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CairoDesktop.Infrastructure.Logging
{
    public class CairoFileLogger : ILogger
    {
        protected readonly CairoFileLoggerProvider _cairoFileProvider;
        private readonly string _name;
        private readonly Dictionary<LogLevel, bool> _logLevelEnabled;

        public CairoFileLogger(CairoFileLoggerProvider cairoFileProvider, string name)
        {
            _cairoFileProvider = cairoFileProvider;
            _name = name;
            var severity = (int)_cairoFileProvider.Options.Severity;
            _logLevelEnabled = new Dictionary<LogLevel, bool>
            {
                { LogLevel.Trace, ((int)LogSeverity.Debug) >= severity },
                { LogLevel.Debug, ((int)LogSeverity.Debug) >= severity },
                { LogLevel.Information, ((int)LogSeverity.Info) >= severity },
                { LogLevel.Warning, ((int)LogSeverity.Warning) >= severity },
                { LogLevel.Error, ((int)LogSeverity.Error) >= severity },
                { LogLevel.Critical, ((int)LogSeverity.Fatal) >= severity },
                { LogLevel.None, false }
            };
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None && _logLevelEnabled[logLevel];
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var fullFilePath = _cairoFileProvider.LogFile;
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"[{DateTime.Now}] [{_name}] [{logLevel}] {formatter(state, exception)}");
            
            if (exception != null)
            {
                stringBuilder.AppendLine("\t:::Exception Details:::");
                foreach (string line in exception.ToString().Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
                    stringBuilder.AppendLine("\t" + line);
            }

            using (var streamWriter = new StreamWriter(fullFilePath, true))
            {
                streamWriter.WriteLine(stringBuilder.ToString());
            }
        }
    }
}
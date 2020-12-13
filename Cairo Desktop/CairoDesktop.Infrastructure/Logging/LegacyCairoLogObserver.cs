using CairoDesktop.Common.Logging;
using Microsoft.Extensions.Logging;

namespace CairoDesktop.Infrastructure.Logging
{
    public class LegacyCairoLogObserver : ILog
    {
        private readonly ILogger<LegacyCairoLogObserver> _logger;

        public LegacyCairoLogObserver(ILogger<LegacyCairoLogObserver> logger)
        {
            _logger = logger;


            CairoLogger.Instance.Attach(this);
        }

        public void Log(object sender, LogEventArgs e)
        {
            LogLevel logLevel = LogLevel.Information;

            switch (e.Severity)
            {
                case Common.Logging.LogSeverity.Debug:
                    logLevel = LogLevel.Debug;
                    break;
                case Common.Logging.LogSeverity.Info:
                    logLevel = LogLevel.Information;
                    break;
                case Common.Logging.LogSeverity.Warning:
                    logLevel = LogLevel.Warning;
                    break;
                case Common.Logging.LogSeverity.Error:
                    logLevel = LogLevel.Error;
                    break;
                case Common.Logging.LogSeverity.Fatal:
                    logLevel = LogLevel.Critical;
                    break;
            }

            _logger.Log(logLevel, e.Exception, e.Message);
        }
    }
}

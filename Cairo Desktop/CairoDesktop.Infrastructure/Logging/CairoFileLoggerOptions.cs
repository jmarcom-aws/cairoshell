namespace CairoDesktop.Infrastructure.Logging
{
    public class CairoFileLoggerOptions
    {
        public virtual string FolderPath { get; set; }

        public LogSeverity Severity { get; set; }
    }
}
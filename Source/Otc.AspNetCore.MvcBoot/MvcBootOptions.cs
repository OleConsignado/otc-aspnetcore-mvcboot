namespace Otc.AspNetCore.MvcBoot
{
    public class MvcBootOptions
    {
        public bool EnableLogging { get; set; } = true;
        public LoggingType LoggingType { get; set; } = LoggingType.MvcBootStdout;
        public bool EnableStringEnumConverter { get; set; } = false;
    }
}
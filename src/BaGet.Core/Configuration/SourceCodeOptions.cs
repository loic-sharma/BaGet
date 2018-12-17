namespace BaGet.Core.Configuration
{
    public class SourceCodeOptions
    {
        public SourceCodeType Type { get; set; }
    }

    public enum SourceCodeType
    {
        Database = 0
    }
}

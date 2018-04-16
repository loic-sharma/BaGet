namespace BaGet.Azure.Configuration
{
    public class AzureSearchOptions : Core.Configuration.SearchOptions
    {
        public string AccountName { get; set; }
        public string ApiKey { get; set; }
    }
}

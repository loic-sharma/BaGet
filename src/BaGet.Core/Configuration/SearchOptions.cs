namespace BaGet.Core.Configuration
{
    public class SearchOptions
    {
        public SearchType Type { get; set; }
    }

    public enum SearchType
    {
        Database = 0,
        Azure = 1,
        Null = 2,
    }
}

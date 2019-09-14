using Newtonsoft.Json;

namespace BaGet.Protocol
{
    public class SearchContext
    {
        public static SearchContext Default(string registrationBaseUrl)
        {
            return new SearchContext
            {
                Vocab = "http://schema.nuget.org/schema#",
                Base = registrationBaseUrl
            };
        }

        [JsonProperty("@vocab")]
        public string Vocab { get; set; }

        [JsonProperty("@base")]
        public string Base { get; set; }
    }
}

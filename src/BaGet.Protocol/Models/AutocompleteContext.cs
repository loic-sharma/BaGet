using System.Text.Json.Serialization;

namespace BaGet.Protocol.Models
{
    public class AutocompleteContext
    {
        public static readonly AutocompleteContext Default = new AutocompleteContext
        {
            Vocab = "http://schema.nuget.org/schema#"
        };

        [JsonPropertyName("@vocab")]
        public string Vocab { get; set; }
    }
}

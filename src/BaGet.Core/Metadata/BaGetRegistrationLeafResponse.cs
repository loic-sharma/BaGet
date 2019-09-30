using BaGet.Protocol.Models;
using Newtonsoft.Json;

namespace BaGet.Core
{
    /// <summary>
    /// BaGet's extensions to a registration leaf response. These additions
    /// are not part of the official protocol.
    /// </summary>
    public class BaGetRegistrationLeafResponse : RegistrationLeafResponse
    {
        [JsonProperty("downloads")]
        public long Downloads { get; set; }
    }
}

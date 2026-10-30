using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;

namespace BaGet.Core;

public class StringArrayToJsonConverter : ValueConverter<string[], string>
{
    public static readonly StringArrayToJsonConverter? Instance = new();

    #pragma warning disable CS8603
    public StringArrayToJsonConverter()
        : base(
            v => JsonConvert.SerializeObject(v),
            v => (!string.IsNullOrEmpty(v)) ? JsonConvert.DeserializeObject<string[]>(v) : new string[0])
    #pragma warning restore CS8603
    {
    }
}
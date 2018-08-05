using BaGet.Core.Configuration;

namespace BaGet.Azure.Configuration
{
    // See: https://github.com/Azure-Samples/active-directory-dotnet-native-aspnetcore/blob/master/TodoListService/Extensions/AzureAdOptions.cs
    public class AzureActiveDirectoryOptions : AuthenticationOptions
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Instance { get; set; }
        public string Domain { get; set; }
        public string TenantId { get; set; }
    }
}

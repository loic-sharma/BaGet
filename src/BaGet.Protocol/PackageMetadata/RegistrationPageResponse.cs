using System.Collections.Generic;
using NuGet.Versioning;

namespace BaGet.Protocol
{
    /// <summary>
    /// A page of package metadata entries.
    /// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource#registration-page
    /// </summary>
    public class RegistrationPageResponse : RegistrationIndexPage
    {
    }
}

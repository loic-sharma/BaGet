using System;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.Frontend.Mvc.Attributes;

/// <summary>
/// Raises the multipart limit for an action that receives a package or artifact file. The host keeps the limit of every other form
/// small (<c>ConfigureBaGetOptions.DefaultFormLimit</c>), because antiforgery validation buffers the whole form of any POST.
/// It is an authorization-stage filter (<see cref="RequestFormLimitsAttribute"/>), so it applies before the antiforgery check
/// reads the form.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class UploadSizeLimitAttribute : RequestFormLimitsAttribute
{
    /// <summary>Allows files up to the request size the server accepts.</summary>
    public UploadSizeLimitAttribute()
    {
        MultipartBodyLengthLimit = long.MaxValue;
    }
}

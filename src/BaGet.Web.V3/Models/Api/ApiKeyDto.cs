using System;
using System.Collections.Generic;

namespace BaGet.Web.V3.Models.Api;

/// <summary>An API key without its secret or hash.</summary>
public sealed record ApiKeyDto(int Key, string? Description, IReadOnlyList<string> Scopes, string? PackageGlobPattern, DateTime Created, DateTime? Expires, string Repositories);

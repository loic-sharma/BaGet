using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A maintenance operation and its last runs.</summary>
public sealed record OperationDto(string Name, string Title, string Description, bool RequiresConfirmation, IReadOnlyList<string> Repositories, IReadOnlyList<AdminJobStatus> Runs);

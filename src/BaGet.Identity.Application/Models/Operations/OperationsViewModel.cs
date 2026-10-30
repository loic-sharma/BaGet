using System;
using System.Collections.Generic;
using System.Linq;
using BaGet.Core;
using BaGet.Identity.Application.Models.Shared;

namespace BaGet.Identity.Application.Models.Operations;

/// <summary>What the operations screen (<c>/admin/operations</c>) shows: the maintenance operations and the status of each.</summary>
public sealed class OperationsViewModel : ConfirmationModel, IFlashModel
{
    /// <summary>The operations available on this server, for every repository.</summary>
    public IReadOnlyList<OperationRow> Rows { get; internal set; } = Array.Empty<OperationRow>();

    /// <summary>Whether any job is running (the page refreshes itself).</summary>
    public bool AnyRunning => Rows.Any(r => r.Status?.State == AdminJobState.Running);

    /// <inheritdoc />
    public string? Message { get; set; }

    /// <inheritdoc />
    public string? Error { get; set; }
}

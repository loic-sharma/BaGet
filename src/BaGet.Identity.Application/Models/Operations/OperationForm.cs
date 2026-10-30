namespace BaGet.Identity.Application.Models.Operations;

/// <summary>Starting a maintenance operation.</summary>
/// <param name="Operation">The operation's name (<c>catalog-rebuild</c>).</param>
/// <param name="Repository">The repository it runs on.</param>
/// <param name="Password">The administrator's password (asked for operations that delete data).</param>
public sealed record OperationForm(string? Operation, string? Repository, string? Password);

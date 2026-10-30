using BaGet.Core;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>One repository with what a user may do there and the grants behind it (the account and user pages).</summary>
/// <param name="Repository">The repository.</param>
/// <param name="Explanation">What the user may do there and which grants say so.</param>
public sealed record RepositoryAccessRow(RepositoryInfo Repository, AccessExplanation Explanation);

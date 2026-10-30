namespace BaGet.Identity.Application.Models.Shared;

/// <summary>A <see cref="IFlashMessage"/> that also carries why the last action was refused (<c>[TempData] Error</c>).</summary>
public interface IFlashModel : IFlashMessage
{
    /// <summary>Why the last action was refused.</summary>
    string? Error { get; set; }
}

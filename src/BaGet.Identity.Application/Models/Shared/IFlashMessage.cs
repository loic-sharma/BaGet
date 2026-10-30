namespace BaGet.Identity.Application.Models.Shared;

/// <summary>
/// A view model that carries the outcome of the last action across a redirect (<c>[TempData] Message</c>). <c>AdminControllerBase.Flash</c> moves the value to and from TempData.
/// </summary>
public interface IFlashMessage
{
    /// <summary>Outcome of the last action.</summary>
    string? Message { get; set; }
}

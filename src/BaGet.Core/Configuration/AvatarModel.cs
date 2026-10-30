namespace BaGet.Core;

/// <summary>
/// What the shared avatar partial renders. A null <see cref="Url"/> means no image is to be
/// requested, and the account's initial is shown instead.
/// </summary>
public record AvatarModel(string Name, string? Url, int Size);

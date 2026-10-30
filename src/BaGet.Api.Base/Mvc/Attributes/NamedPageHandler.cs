using System.Collections.Generic;

namespace BaGet.Frontend.Mvc.Attributes;

/// <summary>A named handler (<c>OnPostApprove</c>-style action) of a controller: its name, the HTTP methods it answers and its screen (see <see cref="PageHandlerAttribute.ScreenOf"/>).</summary>
internal sealed record NamedPageHandler(string Name, IReadOnlyList<string> Verbs, string Screen);

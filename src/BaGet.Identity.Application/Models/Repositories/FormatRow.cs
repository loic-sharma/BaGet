using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Repositories;

/// <summary>One format with what it offers, as the formats screen lists it.</summary>
/// <param name="Module">The format module.</param>
/// <param name="Repositories">How many repositories use it.</param>
/// <param name="Capabilities">The capabilities its services implement.</param>
public sealed record FormatRow(IFormatModule Module, int Repositories, IReadOnlyList<string> Capabilities);

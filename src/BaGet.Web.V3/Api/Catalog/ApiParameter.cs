using System;

namespace BaGet.Web.V3.Api.Catalog;

/// <summary>A parameter of an operation.</summary>
/// <param name="Name">The name on the wire.</param>
/// <param name="In"><c>path</c>, <c>query</c> or <c>body</c>.</param>
/// <param name="Type">The CLR type.</param>
/// <param name="Required">Whether it must be given.</param>
public sealed record ApiParameter(string Name, string In, Type Type, bool Required);

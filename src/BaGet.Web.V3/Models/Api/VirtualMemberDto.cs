namespace BaGet.Web.V3.Models.Api;

/// <summary>One member of a virtual repository.</summary>
public sealed record VirtualMemberDto(string Name, bool Exists);

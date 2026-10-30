using System;

namespace BaGet.Frontend.Mvc.Attributes;

/// <summary>
/// The action answers 404 while no mail is configured (the per-action form of <c>AccountControllerBase.RequiresEmail</c>, for a
/// controller whose screens do not all need it).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresEmailAttribute : Attribute
{
}

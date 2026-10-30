using BaGet.Core;

namespace BaGet.Web.V3.Models.Api;

/// <summary>A policy with its scope's name and the last run.</summary>
public sealed record RetentionPolicyDto(RetentionPolicyView Policy, RetentionJobStatus? LastRun);

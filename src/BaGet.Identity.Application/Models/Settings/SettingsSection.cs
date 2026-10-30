using System.Collections.Generic;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Settings;

/// <summary>One section of the settings screen: its key (empty for the top-level keys), its DOM id and its settings.</summary>
/// <param name="Key">The section's key; empty for the settings that sit at the top level.</param>
/// <param name="Id">The stable DOM id (<see cref="AdminSettingsViewModel.SectionId"/>).</param>
/// <param name="Settings">The settings of the section; a sensitive one never carries its value.</param>
public sealed record SettingsSection(string Key, string Id, IReadOnlyList<SettingState> Settings);

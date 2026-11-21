using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace CreatureComforts;

/// <summary>Status icons from the game's Status sheet, and how many stack variants each one has.</summary>
public static class IconStacks
{
    public sealed record StatusIcon(uint Icon, string Name, int MaxStacks, bool Positive);

    private static List<StatusIcon>? all;
    private static Dictionary<uint, int>? maxStacks;

    /// <summary>One entry per distinct icon, named after the first status that uses it.</summary>
    public static IReadOnlyList<StatusIcon> All
    {
        get
        {
            if (all != null) return all;
            var seen = new Dictionary<uint, StatusIcon>();
            foreach (var s in Plugin.DataManager.GetExcelSheet<Status>())
            {
                if (s.Icon == 0) continue;
                var name = s.Name.ExtractText();
                if (name.Length == 0 || name.StartsWith("_rsv", StringComparison.Ordinal)) continue;
                if (seen.TryGetValue(s.Icon, out var old))
                {
                    if (s.MaxStacks > old.MaxStacks) seen[s.Icon] = old with { MaxStacks = s.MaxStacks };
                    continue;
                }
                seen[s.Icon] = new StatusIcon(s.Icon, name, s.MaxStacks, s.StatusCategory == 1);
            }
            all = seen.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            maxStacks = all.ToDictionary(x => x.Icon, x => x.MaxStacks);
            return all;
        }
    }

    public static int MaxStacks(uint icon)
    {
        _ = All;
        return maxStacks!.TryGetValue(icon, out var m) ? m : 0;
    }

    public static string NameOf(uint icon) => All.FirstOrDefault(x => x.Icon == icon)?.Name ?? $"icon {icon}";
}

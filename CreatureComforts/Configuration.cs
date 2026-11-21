using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace CreatureComforts;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public bool Enabled { get; set; } = true;

    /// <summary>Adds Bladder and Hygiene.</summary>
    public bool Hardcore { get; set; }

    public Dictionary<NeedKind, NeedSettings> NeedSettings { get; set; } = [];

    // ---- Moodles ----

    /// <summary>Below these values a need shows 1, 2 or 3 stacks.</summary>
    public float Threshold1 { get; set; } = 60f;
    public float Threshold2 { get; set; } = 35f;
    public float Threshold3 { get; set; } = 15f;

    /// <summary>Put your own (non-Creature Comforts) Moodles back if Moodles drops them on a zone change.</summary>
    public bool ProtectOtherMoodles { get; set; } = true;

    public bool ShowSerene { get; set; } = true;
    public uint SereneIcon { get; set; } = Needs.SereneIcon;
    public string SereneTitle { get; set; } = Needs.SereneTitle;
    public string SereneDescription { get; set; } = Needs.SereneDescription;

    // ---- Refill and drain tuning ----

    /// <summary>Sleep drains this many times faster in duties.</summary>
    public float SleepDutyMultiplier { get; set; } = 3f;

    /// <summary>Minutes of /doze in bed to go from empty to full.</summary>
    public float BedRefillMinutes { get; set; } = 10f;

    /// <summary>Minutes logged out in a rest spot to go from empty to full sleep.</summary>
    public float OfflineRefillMinutes { get; set; } = 30f;

    /// <summary>Minutes in a party to go from empty to full social.</summary>
    public float PartyRefillMinutes { get; set; } = 30f;

    /// <summary>Minutes at a venue to go from empty to full fun.</summary>
    public float VenueRefillMinutes { get; set; } = 30f;

    /// <summary>Minutes of dancing to go from empty to full fun.</summary>
    public float DanceRefillMinutes { get; set; } = 40f;

    public float FunPerMgpWin { get; set; } = 15f;
    public float FunPerToyChest { get; set; } = 25f;
    public float FunPerDance { get; set; } = 3f;
    public float SocialPerChat { get; set; } = 3f;
    public float SocialPerEmote { get; set; } = 5f;

    /// <summary>Percent per second Bladder and Hygiene fill while you hold their emote or pose.</summary>
    public float HoldRefillPerSecond { get; set; } = 10f;

    /// <summary>Hygiene drains this many times faster in combat.</summary>
    public float HygieneCombatMultiplier { get; set; } = 2f;

    /// <summary>Extra emote names (besides anything with "dance" in its name) that count as dancing.</summary>
    public List<string> ExtraDanceEmotes { get; set; } = ["Bee's Knees", "Most Gentlemanly", "Songbird", "Box Step", "Side Step", "Get Fantasy", "Popotostep", "Paint It Red", "Paint It Blue", "Paint It Yellow", "Paint It Black"];

    // ---- Places ----

    /// <summary>Logging out inside any house (not only your own) counts as rest.</summary>
    public bool AnyHouseIsRestSpot { get; set; }

    public List<Venue> Venues { get; set; } = [];

    // ---- Saved state per character (content id) ----

    public Dictionary<ulong, CharacterNeeds> Characters { get; set; } = [];

    public NeedSettings For(NeedKind kind)
    {
        if (!NeedSettings.TryGetValue(kind, out var s))
        {
            var info = Needs.Info(kind);
            s = new NeedSettings
            {
                HoursToEmpty = info.DefaultHours,
                Icon = info.DefaultIcon,
                Titles = [.. info.DefaultTitles],
                Description = info.DefaultDescription,
            };
            NeedSettings[kind] = s;
        }
        return s;
    }

    /// <summary>Brings older settings up to date. Icons only change if they were still the old defaults.</summary>
    public void Migrate()
    {
        if (Version < 2)
        {
            // 0.1.2: new default icons for Social (Brand of the Sullen), Hygiene (Concentrated Poison) and Serene (Diamondback).
            if (NeedSettings.TryGetValue(NeedKind.Social, out var social) && social.Icon == 218961) social.Icon = 218041;
            if (NeedSettings.TryGetValue(NeedKind.Hygiene, out var hygiene) && hygiene.Icon == 218881) hygiene.Icon = 218581;
            if (SereneIcon == 217311) SereneIcon = 213509;
            Version = 2;
            Save();
        }
        if (Version < 3)
        {
            // 0.1.6: a pose captured mid-movement was saved as type 255, which "matched" whenever you moved.
            foreach (var s in NeedSettings.Values)
                if (s.RefillPoseType > 6) s.RefillPoseType = -1;
            Version = 3;
            Save();
        }
    }

    public bool IsActive(NeedKind kind) => For(kind).Enabled && (Hardcore || !Needs.Info(kind).Hardcore);

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public class NeedSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Real-time hours to go from full to empty.</summary>
    public float HoursToEmpty { get; set; } = 4f;

    /// <summary>Emote that fills this need instantly (hardcore needs). 0 = none.</summary>
    public uint RefillEmote { get; set; }

    /// <summary>/cpose idle that fills this need instantly: pose type (EmoteController.PoseType; -1 = none)
    /// and the pose number within it (EmoteController.CPoseState, 0-based).</summary>
    public int RefillPoseType { get; set; } = -1;
    public byte RefillPoseIndex { get; set; }

    public uint Icon { get; set; }

    /// <summary>Moodle title at 1, 2 and 3 stacks.</summary>
    public List<string> Titles { get; set; } = [];

    public string Description { get; set; } = string.Empty;

    public string TitleFor(int level, NeedKind kind)
    {
        var i = Math.Clamp(level, 1, 3) - 1;
        if (i < Titles.Count && !string.IsNullOrWhiteSpace(Titles[i])) return Titles[i];
        return Needs.Info(kind).DefaultTitles[i];
    }
}

[Serializable]
public class CharacterNeeds
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<NeedKind, float> Values { get; set; } = [];

    /// <summary>Last time the plugin saw this character logged in.</summary>
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Whether the character was in an inn room or at home when last seen.</summary>
    public bool LastInRestSpot { get; set; }

    public float Get(NeedKind kind) => Values.TryGetValue(kind, out var v) ? v : 100f;

    public void Set(NeedKind kind, float value) => Values[kind] = Math.Clamp(value, 0f, 100f);
}

[Serializable]
public class Venue
{
    /// <summary>The game's house id (HouseId.Id).</summary>
    public ulong HouseId { get; set; }

    public string Name { get; set; } = string.Empty;
}

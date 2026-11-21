using System;
using System.Collections.Generic;
using System.Linq;

namespace CreatureComforts;

public enum NeedKind
{
    Hunger,
    Sleep,
    Fun,
    Social,
    Bladder,
    Hygiene,
}

/// <summary>Fixed facts about each need, plus its default settings.</summary>
public sealed record NeedInfo(
    NeedKind Kind,
    string Name,
    bool Hardcore,
    float DefaultHours,
    uint DefaultIcon,
    string[] DefaultTitles,
    string DefaultDescription,
    string HowToFill,
    Guid MoodleGuid);

public static class Needs
{
    // Status icons that have stack variants in the game (icon + 1 = 2 stacks, and so on).
    // Moodles shows stacks by picking icon + stacks - 1, so these must be stackable icons.
    public static readonly IReadOnlyList<NeedInfo> All =
    [
        new(NeedKind.Hunger, "Hunger", false, 3f, 218081,
            ["Peckish", "Hungry", "Starving"],
            "Eat or drink something to become Well Fed.",
            "Eat or drink anything that gives Well Fed. Stays full until Well Fed wears off.",
            new Guid("c0f0c0f0-0001-4c43-9e65-6564730a0001")),
        new(NeedKind.Sleep, "Sleep", false, 8f, 220221,
            ["Drowsy", "Tired", "Exhausted"],
            "Get some rest: /doze on a bed, or log out in an inn room or at home.",
            "Lie down with /doze on a bed, or log out in an inn room or your own home. Drains faster in duties.",
            new Guid("c0f0c0f0-0002-4c43-9e65-6564730a0002")),
        new(NeedKind.Fun, "Fun", false, 4f, 217471,
            ["Restless", "Bored", "Miserably Bored"],
            "Have some fun: the Gold Saucer, a dance, or a night out at a venue.",
            "Win MGP (GATEs, Triple Triad, races, Verminion, game machines), the inn's Toy Chest, dance emotes, or time at a venue.",
            new Guid("c0f0c0f0-0003-4c43-9e65-6564730a0003")),
        new(NeedKind.Social, "Social", false, 4f, 218041,
            ["Lonely", "Very Lonely", "Isolated"],
            "Spend time with people: join a party, chat, or emote at someone.",
            "Being in a party, sending chat messages, or using emotes while targeting another player.",
            new Guid("c0f0c0f0-0004-4c43-9e65-6564730a0004")),
        new(NeedKind.Bladder, "Bladder", true, 2f, 217641,
            ["Needs the Restroom", "Urgent", "Desperate"],
            "Find a restroom.",
            "Hold your chosen emote (a looping one) or /cpose idle; it fills a little every second you hold it.",
            new Guid("c0f0c0f0-0005-4c43-9e65-6564730a0005")),
        new(NeedKind.Hygiene, "Hygiene", true, 6f, 218581,
            ["Grubby", "Smelly", "Filthy"],
            "Time for a bath.",
            "Hold your chosen emote (a looping one) or /cpose idle; it fills a little every second you hold it. Drains faster in combat.",
            new Guid("c0f0c0f0-0006-4c43-9e65-6564730a0006")),
    ];

    public static readonly Guid SereneGuid = new("c0f0c0f0-0007-4c43-9e65-6564730a0007");
    public const uint SereneIcon = 213509;
    public const string SereneTitle = "Serene";
    public const string SereneDescription = "Every need is met. Life is good.";

    public static NeedInfo Info(NeedKind kind) => All.First(n => n.Kind == kind);

    public static IEnumerable<Guid> AllMoodleGuids => All.Select(n => n.MoodleGuid).Append(SereneGuid);
}

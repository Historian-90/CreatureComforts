using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using IntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace CreatureComforts;

/// <summary>Small read-only questions about the game state. Everything here is polling, no hooks.</summary>
public static class GameInfo
{
    public const uint WellFedStatus = 48;
    public const uint MgpItem = 29;

    private static byte? bedMode;
    private static Dictionary<uint, string>? emoteNames;

    /// <summary>English emote names by row (for dance matching), and the client-language ones for display.</summary>
    public static IReadOnlyDictionary<uint, string> EmoteNamesEnglish
    {
        get
        {
            if (emoteNames != null) return emoteNames;
            emoteNames = [];
            foreach (var e in Plugin.DataManager.GetExcelSheet<Emote>(ClientLanguage.English))
            {
                var name = e.Name.ExtractText();
                if (name.Length > 0) emoteNames[e.RowId] = name;
            }
            return emoteNames;
        }
    }

    public static string EmoteName(uint id)
    {
        if (id == 0) return "none";
        return Plugin.DataManager.GetExcelSheet<Emote>().TryGetRow(id, out var e) && e.Name.ExtractText() is { Length: > 0 } n
            ? n
            : $"emote {id}";
    }

    /// <summary>Emotes the player could pick, sorted by name: (row, display name, command).</summary>
    public static List<(uint Id, string Name, string Command)> PickableEmotes()
    {
        var list = new List<(uint, string, string)>();
        foreach (var e in Plugin.DataManager.GetExcelSheet<Emote>())
        {
            var name = e.Name.ExtractText();
            if (name.Length == 0 || e.Order == 0) continue;
            var cmd = e.TextCommand.ValueNullable?.Command.ExtractText() ?? string.Empty;
            list.Add((e.RowId, name, cmd));
        }
        return list.OrderBy(x => x.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool IsDance(uint emoteId, Configuration config)
    {
        if (emoteId == 0 || !EmoteNamesEnglish.TryGetValue(emoteId, out var name)) return false;
        if (name.Contains("dance", StringComparison.OrdinalIgnoreCase)) return true;
        return config.ExtraDanceEmotes.Any(x => string.Equals(x.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The EmoteMode row used when you /doze on a bed (its start emote is "Sleep"; row 3 when checked).</summary>
    public static byte BedMode
    {
        get
        {
            if (bedMode.HasValue) return bedMode.Value;
            bedMode = 3;
            foreach (var m in Plugin.DataManager.GetExcelSheet<EmoteMode>(ClientLanguage.English))
            {
                if (m.ConditionMode == (byte)CharacterModes.InPositionLoop &&
                    m.StartEmote.ValueNullable?.Name.ExtractText() == "Sleep")
                {
                    bedMode = (byte)m.RowId;
                    break;
                }
            }
            return bedMode.Value;
        }
    }

    public static unsafe (CharacterModes Mode, byte Param, ushort Emote) Pose(IPlayerCharacter me)
    {
        var chara = (Character*)me.Address;
        return (chara->Mode, chara->ModeParam, chara->EmoteController.EmoteId);
    }

    public static bool InBed(IPlayerCharacter me)
    {
        var p = Pose(me);
        return p.Mode == CharacterModes.InPositionLoop && p.Param == BedMode;
    }

    public static bool HasStatus(IPlayerCharacter me, uint statusId) =>
        me.StatusList.Any(s => s.StatusId == statusId);

    public static TimeSpan WellFedLeft(IPlayerCharacter me)
    {
        foreach (var s in me.StatusList)
            if (s.StatusId == WellFedStatus) return TimeSpan.FromSeconds(Math.Max(0, s.RemainingTime));
        return TimeSpan.Zero;
    }

    public static unsafe long Mgp()
    {
        var im = InventoryManager.Instance();
        return im == null ? -1 : im->GetInventoryItemCount(MgpItem);
    }

    public static unsafe bool InParty()
    {
        if (Plugin.PartyList.Length > 1) return true;
        var cr = InfoProxyCrossRealm.Instance();
        return cr != null && cr->IsInCrossRealmParty && InfoProxyCrossRealm.GetPartyMemberCount() > 1;
    }

    public static IntendedUse? PlaceKind()
    {
        if (!Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(Plugin.ClientState.TerritoryType, out var t))
            return null;
        return (IntendedUse)t.TerritoryIntendedUse.RowId;
    }

    public static bool InDuty()
    {
        var kind = PlaceKind();
        if (kind is IntendedUse.IslandSanctuary or IntendedUse.Inn or IntendedUse.HousingIndoor or IntendedUse.GoldSaucer)
            return false;
        return Plugin.Condition[ConditionFlag.BoundByDuty] || Plugin.Condition[ConditionFlag.BoundByDuty56] ||
               Plugin.Condition[ConditionFlag.BoundByDuty95];
    }

    public static bool InCombat() => Plugin.Condition[ConditionFlag.InCombat];

    public static bool InGoldSaucer() => PlaceKind() is IntendedUse.GoldSaucer or IntendedUse.ChocoboRacing
        or IntendedUse.LordOfVerminion or IntendedUse.TripleTriadBattlehall;

    // ---- Houses ----

    public static unsafe HouseId? CurrentHouse()
    {
        var hm = HousingManager.Instance();
        if (hm == null || !hm->IsInside()) return null;
        var id = hm->GetCurrentIndoorHouseId();
        return id.Id == 0 || id.Id == ulong.MaxValue ? null : id;
    }

    public static bool SameHome(HouseId a, HouseId b)
    {
        if (a.WorldId != 0 && b.WorldId != 0 && a.WorldId != b.WorldId) return false;
        if (a.TerritoryTypeId != b.TerritoryTypeId || a.WardIndex != b.WardIndex || a.IsApartment != b.IsApartment)
            return false;
        return a.IsApartment
            ? a.ApartmentDivision == b.ApartmentDivision && a.RoomNumber == b.RoomNumber
            : a.PlotIndex == b.PlotIndex;
    }

    public static bool IsOwnedByMe(HouseId here)
    {
        var owned = new[]
        {
            HousingManager.GetOwnedHouseId(EstateType.PersonalEstate, 0),
            HousingManager.GetOwnedHouseId(EstateType.FreeCompanyEstate, 0),
            HousingManager.GetOwnedHouseId(EstateType.PersonalChambers, 0),
            HousingManager.GetOwnedHouseId(EstateType.ApartmentRoom, 0),
            HousingManager.GetOwnedHouseId(EstateType.SharedEstate, 0),
            HousingManager.GetOwnedHouseId(EstateType.SharedEstate, 1),
        };
        return owned.Any(o => o.Id != 0 && o.Id != ulong.MaxValue && SameHome(o, here));
    }

    public static string HouseName(HouseId id)
    {
        var district = Plugin.DataManager.GetExcelSheet<TerritoryType>().TryGetRow(id.TerritoryTypeId, out var t)
            ? t.PlaceName.ValueNullable?.Name.ExtractText() ?? "Housing"
            : "Housing";
        return id.IsApartment
            ? $"{district}, Ward {id.WardIndex + 1}{(id.ApartmentDivision == 1 ? " (Subdivision)" : "")}, Apartment {id.RoomNumber}"
            : $"{district}, Ward {id.WardIndex + 1}, Plot {id.PlotIndex + 1}";
    }

    public static Venue? VenueHere(Configuration config)
    {
        if (CurrentHouse() is not { } here) return null;
        return config.Venues.FirstOrDefault(v => SameHome((HouseId)v.HouseId, here));
    }

    /// <summary>Inn room, your own home, or (if allowed) any house interior.</summary>
    public static bool InRestSpot(Configuration config)
    {
        if (PlaceKind() == IntendedUse.Inn) return true;
        if (CurrentHouse() is not { } here) return false;
        return config.AnyHouseIsRestSpot || IsOwnedByMe(here);
    }

    public static string Describe()
    {
        var kind = PlaceKind();
        var house = CurrentHouse();
        var where = house is { } h ? HouseName(h) + (IsOwnedByMe(h) ? " (yours)" : "") : kind?.ToString() ?? "unknown";
        return where;
    }
}

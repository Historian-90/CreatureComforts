using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Chat;

namespace CreatureComforts;

/// <summary>
/// Runs the needs: drains them over real time while you're logged in, refills them from what you do,
/// and works out how many Moodle stacks each one should show.
/// </summary>
public sealed class NeedTracker : IDisposable
{
    private readonly Plugin plugin;
    private Configuration Config => plugin.Configuration;

    private ulong contentId;
    private CharacterNeeds? current;
    private DateTime lastTick;
    private DateTime lastSave = DateTime.UtcNow;

    // Edge detection
    private ushort lastEmote;
    private long lastMgp = -1;
    private bool wasInEvent;
    private DateTime chatCooldown = DateTime.MinValue;
    private DateTime emoteSocialCooldown = DateTime.MinValue;
    private DateTime toyChestCooldown = DateTime.MinValue;
    private readonly Dictionary<NeedKind, int> levels = [];

    // For the Testing page
    public string LastActivity { get; private set; } = "Nothing yet.";
    public string LastEmoteSeen { get; private set; } = "none";
    public bool Sleeping { get; private set; }
    public bool Dancing { get; private set; }
    public bool AtVenue { get; private set; }
    public bool InParty { get; private set; }
    public bool RestSpot { get; private set; }

    /// <summary>Fired when a need's stack level (0-3) changes: (need name, level).</summary>
    public event Action<NeedKind, int>? LevelChanged;

    public NeedTracker(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.ChatGui.ChatMessage += OnChat;
        Plugin.ClientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        Plugin.ChatGui.ChatMessage -= OnChat;
        Plugin.ClientState.Logout -= OnLogout;
        Save();
    }

    public CharacterNeeds? Current => current;

    public int Level(NeedKind kind) => levels.TryGetValue(kind, out var l) ? l : 0;

    public IEnumerable<NeedKind> ActiveNeeds => Needs.All.Select(n => n.Kind).Where(Config.IsActive);

    public void Tick(IPlayerCharacter? me)
    {
        var now = DateTime.UtcNow;
        if (me == null || !Plugin.ClientState.IsLoggedIn || Plugin.PlayerState.ContentId == 0)
        {
            lastTick = now;
            return;
        }

        if (Plugin.PlayerState.ContentId != contentId) StartSession(me, now);
        if (current == null) return;

        var dt = (float)Math.Clamp((now - lastTick).TotalSeconds, 0, 5);
        lastTick = now;

        Detect(me, now);
        if (Config.Enabled) Simulate(dt);

        RestSpot = GameInfo.InRestSpot(Config);
        current.LastSeenUtc = now;
        current.LastInRestSpot = RestSpot;
        UpdateLevels();

        if (now - lastSave > TimeSpan.FromSeconds(30))
        {
            Save();
            lastSave = now;
        }
    }

    private void StartSession(IPlayerCharacter me, DateTime now)
    {
        Save();
        contentId = Plugin.PlayerState.ContentId;
        lastTick = now;
        lastEmote = GameInfo.Pose(me).Emote;
        lastMgp = GameInfo.Mgp();
        levels.Clear();

        if (!Config.Characters.TryGetValue(contentId, out var c))
        {
            c = new CharacterNeeds { LastSeenUtc = now };
            Config.Characters[contentId] = c;
            LastActivity = "New character: every need starts full.";
        }
        else if (c.LastInRestSpot && c.LastSeenUtc != default)
        {
            // Logged out in an inn room or at home: that counts as sleep.
            var away = now - c.LastSeenUtc;
            if (away > TimeSpan.FromMinutes(1))
            {
                var gain = (float)(away.TotalMinutes / Math.Max(1, Config.OfflineRefillMinutes) * 100);
                c.Set(NeedKind.Sleep, c.Get(NeedKind.Sleep) + gain);
                LastActivity = $"Rested while logged out ({away.TotalMinutes:0} min): Sleep +{Math.Min(gain, 100):0}.";
            }
        }
        c.Name = me.Name.TextValue;
        current = c;
        plugin.Moodles.Reset();
    }

    private void OnLogout(int type, int code)
    {
        Save();
        contentId = 0;
        current = null;
        levels.Clear();
    }

    public void Save()
    {
        try { Config.Save(); }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Couldn't save"); }
    }

    // ---- Things you do ----------------------------------------------------------------------------

    private void Detect(IPlayerCharacter me, DateTime now)
    {
        var pose = GameInfo.Pose(me);

        Sleeping = pose.Mode == FFXIVClientStructs.FFXIV.Client.Game.Character.CharacterModes.InPositionLoop &&
                   pose.Param == GameInfo.BedMode;
        Dancing = GameInfo.IsDance(pose.Emote, Config);
        AtVenue = GameInfo.VenueHere(Config) != null;
        InParty = GameInfo.InParty();

        // Emotes: react when a new one starts.
        if (pose.Emote != lastEmote)
        {
            lastEmote = pose.Emote;
            if (pose.Emote != 0) OnEmote(me, pose.Emote, now);
        }

        // MGP going up = you won something at the Gold Saucer (GATEs, Triple Triad, races, machines...).
        var mgp = GameInfo.Mgp();
        if (mgp >= 0)
        {
            if (lastMgp >= 0 && mgp > lastMgp && Config.IsActive(NeedKind.Fun))
            {
                Add(NeedKind.Fun, Config.FunPerMgpWin);
                LastActivity = $"Won {mgp - lastMgp:N0} MGP: Fun +{Config.FunPerMgpWin:0}.";
            }
            lastMgp = mgp;
        }

        // The inn room's Toy Chest (gives no MGP): starting to use it while it's your target.
        var inEvent = Plugin.Condition[ConditionFlag.OccupiedInEvent];
        if (inEvent && !wasInEvent && now >= toyChestCooldown &&
            Plugin.TargetManager.Target?.Name.TextValue.Contains("Toy Chest", StringComparison.OrdinalIgnoreCase) == true)
        {
            toyChestCooldown = now + TimeSpan.FromSeconds(30);
            Add(NeedKind.Fun, Config.FunPerToyChest);
            LastActivity = $"Played with the Toy Chest: Fun +{Config.FunPerToyChest:0}.";
        }
        wasInEvent = inEvent;
    }

    private void OnEmote(IPlayerCharacter me, ushort emote, DateTime now)
    {
        LastEmoteSeen = $"{GameInfo.EmoteName(emote)} ({emote})";

        foreach (var kind in new[] { NeedKind.Bladder, NeedKind.Hygiene })
        {
            var s = Config.For(kind);
            if (Config.IsActive(kind) && s.RefillEmote != 0 && s.RefillEmote == emote)
            {
                current!.Set(kind, 100);
                LastActivity = $"{GameInfo.EmoteName(emote)}: {Needs.Info(kind).Name} refilled.";
            }
        }

        if (GameInfo.IsDance(emote, Config))
        {
            Add(NeedKind.Fun, Config.FunPerDance);
            LastActivity = $"Dancing ({GameInfo.EmoteName(emote)}): Fun +{Config.FunPerDance:0}.";
        }

        // Emoting at another player is social.
        if (now >= emoteSocialCooldown && Plugin.TargetManager.Target is IPlayerCharacter other && other.Address != me.Address)
        {
            emoteSocialCooldown = now + TimeSpan.FromSeconds(10);
            Add(NeedKind.Social, Config.SocialPerEmote);
            LastActivity = $"{GameInfo.EmoteName(emote)} at {other.Name.TextValue}: Social +{Config.SocialPerEmote:0}.";
        }
    }

    private static readonly HashSet<XivChatType> SocialChannels =
    [
        XivChatType.Say, XivChatType.Shout, XivChatType.Yell, XivChatType.Party, XivChatType.CrossParty,
        XivChatType.Alliance, XivChatType.FreeCompany, XivChatType.TellOutgoing, XivChatType.CustomEmote,
        XivChatType.StandardEmote, XivChatType.NoviceNetwork, XivChatType.PvPTeam,
        XivChatType.Ls1, XivChatType.Ls2, XivChatType.Ls3, XivChatType.Ls4,
        XivChatType.Ls5, XivChatType.Ls6, XivChatType.Ls7, XivChatType.Ls8,
        XivChatType.CrossLinkShell1, XivChatType.CrossLinkShell2, XivChatType.CrossLinkShell3, XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5, XivChatType.CrossLinkShell6, XivChatType.CrossLinkShell7, XivChatType.CrossLinkShell8,
    ];

    private void OnChat(IHandleableChatMessage message)
    {
        try
        {
            if (current == null || !Config.Enabled || !Config.IsActive(NeedKind.Social)) return;
            var type = message.LogKind;
            if (!SocialChannels.Contains(type)) return;
            var mine = type == XivChatType.TellOutgoing || message.SourceKind == XivChatRelationKind.LocalPlayer;
            if (!mine) return;

            var now = DateTime.UtcNow;
            if (now < chatCooldown) return;
            chatCooldown = now + TimeSpan.FromSeconds(10);
            Add(NeedKind.Social, Config.SocialPerChat);
            LastActivity = $"Chatted ({type}): Social +{Config.SocialPerChat:0}.";
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Chat check failed");
        }
    }

    // ---- Drain and refill over time ---------------------------------------------------------------

    private void Simulate(float dt)
    {
        var c = current!;
        var me = Plugin.ObjectTable.LocalPlayer!;
        var inDuty = GameInfo.InDuty();

        foreach (var kind in ActiveNeeds)
        {
            var s = Config.For(kind);
            var drain = 100f / (Math.Max(0.1f, s.HoursToEmpty) * 3600f) * dt;
            var gain = 0f;

            switch (kind)
            {
                case NeedKind.Hunger:
                    if (GameInfo.HasStatus(me, GameInfo.WellFedStatus))
                    {
                        drain = 0;
                        gain = 100; // Well Fed keeps you full until it wears off.
                    }
                    break;
                case NeedKind.Sleep:
                    if (Sleeping)
                    {
                        drain = 0;
                        gain = Rate(Config.BedRefillMinutes, dt);
                    }
                    else if (inDuty)
                    {
                        drain *= Config.SleepDutyMultiplier;
                    }
                    break;
                case NeedKind.Fun:
                    if (AtVenue) { drain = 0; gain += Rate(Config.VenueRefillMinutes, dt); }
                    if (Dancing) { drain = 0; gain += Rate(Config.DanceRefillMinutes, dt); }
                    if (GameInfo.InGoldSaucer()) drain = 0;
                    break;
                case NeedKind.Social:
                    if (InParty)
                    {
                        drain = 0;
                        gain = Rate(Config.PartyRefillMinutes, dt);
                    }
                    break;
                case NeedKind.Hygiene:
                    if (GameInfo.InCombat()) drain *= Config.HygieneCombatMultiplier;
                    break;
            }

            c.Set(kind, c.Get(kind) - drain + gain);
        }
    }

    private static float Rate(float minutesToFull, float dt) => 100f / (Math.Max(0.1f, minutesToFull) * 60f) * dt;

    public void Add(NeedKind kind, float amount)
    {
        if (current == null || !Config.IsActive(kind)) return;
        current.Set(kind, current.Get(kind) + amount);
    }

    public void SetValue(NeedKind kind, float value) => current?.Set(kind, value);

    // ---- Levels (Moodle stacks) -------------------------------------------------------------------

    private void UpdateLevels()
    {
        foreach (var info in Needs.All)
        {
            var old = Level(info.Kind);
            var level = Config.Enabled && Config.IsActive(info.Kind) ? LevelFor(current!.Get(info.Kind), old) : 0;
            if (level == old) continue;
            levels[info.Kind] = level;
            try { LevelChanged?.Invoke(info.Kind, level); }
            catch (Exception ex) { Plugin.Log.Warning(ex, "A LevelChanged listener failed"); }
        }
    }

    /// <summary>0 = fine, 1-3 = how bad. Going back up needs a little margin so stacks don't flicker.</summary>
    private int LevelFor(float value, int old)
    {
        float[] t = [Config.Threshold1, Config.Threshold2, Config.Threshold3];
        var level = 0;
        for (var i = 0; i < 3; i++)
            if (value < t[i]) level = i + 1;

        if (level < old)
        {
            const float margin = 2f;
            var lvl = 0;
            for (var i = 0; i < 3; i++)
                if (value < t[i] + margin) lvl = i + 1;
            level = Math.Min(old, lvl);
        }
        return level;
    }

    public List<WantedMoodle> WantedMoodles()
    {
        var list = new List<WantedMoodle>();
        if (current == null || !Config.Enabled) return list;

        var anyActive = false;
        var allFine = true;
        foreach (var info in Needs.All)
        {
            if (!Config.IsActive(info.Kind)) continue;
            anyActive = true;
            var level = Level(info.Kind);
            if (level == 0) continue;
            allFine = false;
            var s = Config.For(info.Kind);
            var stacks = IconStacks.MaxStacks(s.Icon) >= 3 ? level : 1;
            list.Add(new WantedMoodle(info.MoodleGuid, s.Icon, s.TitleFor(level, info.Kind),
                                      string.IsNullOrWhiteSpace(s.Description) ? info.DefaultDescription : s.Description,
                                      stacks, false));
        }

        if (anyActive && allFine && Config.ShowSerene)
            list.Add(new WantedMoodle(Needs.SereneGuid, Config.SereneIcon, Config.SereneTitle, Config.SereneDescription, 1, true));

        return list;
    }
}

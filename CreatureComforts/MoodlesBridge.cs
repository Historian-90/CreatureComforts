using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Ipc.Exceptions;

namespace CreatureComforts;

// Matches Moodles' MoodlesStatusInfo tuple; its enum fields (Type, ChainTrigger) are plain ints here.
using MoodleData = (int Version, Guid GUID, int IconID, string Title, string Description, string CustomVFXPath,
    long ExpireTicks, int Type, int Stacks, int StackSteps, uint Modifiers, Guid ChainedStatus, int ChainTrigger,
    string Applier, string Dispeller, bool Permanent);

/// <summary>What one Moodle should look like.</summary>
public sealed record WantedMoodle(Guid Guid, uint Icon, string Title, string Description, int Stacks, bool Positive);

/// <summary>
/// Puts our needs on the player as Moodles through Moodles' public IPC, and takes them off again.
///
/// Moodles only accepts statuses from other plugins when its "Allow other plugins apply Moodles" setting is on
/// AND it allows the applier (party/friends/all); we apply as the player themselves, who counts as a party
/// member, so "party" or "all" is enough.
///
/// Zone changes: any IPC call marks the player's Moodles list as "touched by IPC", and Moodles deletes such
/// lists when the character unloads (meant for synced friends, but it hits your own character too). So on
/// every zone change Moodles drops ALL your Moodles, ours and your own. We put them back as soon as the new
/// character exists, ideally while the loading screen is still up: Moodles doesn't show its "applied" pop-up
/// while you're loading, so they come back quietly.
/// </summary>
public sealed class MoodlesBridge
{
    private const int MoodleVersion = 2;
    private const int Positive = 0;
    private const int Negative = 1;

    private readonly Configuration config;

    /// <summary>What we last asked Moodles to show (guid -> moodle).</summary>
    private readonly Dictionary<Guid, WantedMoodle> sent = [];

    private DateTime nextCheck = DateTime.MinValue;
    private DateTime lastApply = DateTime.MinValue;
    private bool cleanedUp;
    private DateTime retryAt = DateTime.MinValue;

    // Zone changes
    private nint lastAddress;
    private uint lastTerritory;
    private DateTime settleUntil = DateTime.MinValue;   // right after a zone change: check often, don't cry "blocked"
    private DateTime nextLoadingCheck = DateTime.MinValue;

    /// <summary>Your other Moodles (not ours), as last seen, so they can be put back after a zone change.</summary>
    private List<MoodleData> othersSnapshot = [];
    private DateTime nextSnapshot = DateTime.MinValue;

    /// <summary>How many checks in a row found everything we sent missing (for the "blocked" diagnosis).</summary>
    private int missingStreak;

    /// <summary>The last thing we changed in Moodles, and why (shown on the Moodles page).</summary>
    public string LastChange { get; private set; } = "Nothing yet.";

    public string Status { get; private set; } = "Not checked yet.";
    public bool Available { get; private set; }

    /// <summary>True when Moodles keeps turning ours away: its settings are probably blocking us.</summary>
    public bool Refused { get; private set; }

    public int? MoodlesApiVersion { get; private set; }

    public MoodlesBridge(Configuration config) => this.config = config;

    /// <summary>Called every tick with what should be showing. Sends only changes, and checks now and then.</summary>
    public void Sync(IPlayerCharacter me, IReadOnlyList<WantedMoodle> wanted)
    {
        var now = DateTime.UtcNow;
        if (now < retryAt) return;

        try
        {
            var territory = Plugin.ClientState.TerritoryType;
            var zoned = me.Address != lastAddress || territory != lastTerritory;
            if (zoned)
            {
                lastAddress = me.Address;
                lastTerritory = territory;
                settleUntil = now + TimeSpan.FromSeconds(20);
                missingStreak = 0;
            }

            // During a loading screen: only put back what Moodles dropped (quietly), nothing else.
            if (Loading())
            {
                if (zoned || now >= nextLoadingCheck)
                {
                    nextLoadingCheck = now + TimeSpan.FromSeconds(0.5);
                    Restore(me, wanted, "loading");
                }
                return;
            }

            if (zoned) Restore(me, wanted, "zone change");

            var wantedIds = wanted.Select(w => w.Guid).ToHashSet();
            if (!cleanedUp)
            {
                // Start clean, but gently: only take off our leftovers that aren't wanted any more. Ones that are
                // still wanted get updated in place, which doesn't make Moodles show its "applied" pop-up again.
                Remove(me, Needs.AllMoodleGuids.Where(g => !wantedIds.Contains(g)).ToList());
                sent.Clear();
                cleanedUp = true;
            }

            var stale = sent.Keys.Where(g => !wantedIds.Contains(g)).ToList();
            if (stale.Count > 0)
            {
                LastChange = $"{Stamp()} Took off {string.Join(", ", stale.Select(g => sent[g].Title))} (need changed).";
                Remove(me, stale);
                foreach (var g in stale) sent.Remove(g);
            }

            foreach (var w in wanted)
            {
                if (sent.TryGetValue(w.Guid, out var old) && old == w) continue;
                LastChange = $"{Stamp()} {(old == null ? "Put on" : "Updated")} {w.Title}{(w.Stacks > 1 ? $" ({w.Stacks} stacks)" : "")}.";
                Apply(me, w);
                sent[w.Guid] = w;
                lastApply = now;
            }

            // Check that ours are really there (every 15 s; every 2 s just after a zone change).
            if (now >= nextCheck && now - lastApply > TimeSpan.FromSeconds(1.5))
            {
                nextCheck = now + TimeSpan.FromSeconds(now < settleUntil ? 2 : 15);
                Verify(me, now);
            }

            // Remember your other Moodles so a zone change can't take them away.
            if (now >= nextSnapshot && now >= settleUntil)
            {
                nextSnapshot = now + TimeSpan.FromSeconds(3);
                var ours = Needs.AllMoodleGuids.ToHashSet();
                othersSnapshot = OnMe(me).Values.Where(m => !ours.Contains(m.GUID)).ToList();
            }

            Available = true;
        }
        catch (IpcNotReadyError)
        {
            Available = false;
            Refused = false;
            Status = "Moodles isn't installed or isn't loaded. Install Moodles to see your needs on your character.";
            sent.Clear();
            cleanedUp = false;
            retryAt = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        }
        catch (Exception ex)
        {
            Available = false;
            Status = "Couldn't talk to Moodles: " + ex.Message;
            Plugin.Log.Warning(ex, "Moodles IPC failed");
            retryAt = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            sent.Clear();
            cleanedUp = false;
        }
    }

    /// <summary>Puts back ours (and, if allowed, your own) Moodles that Moodles dropped.</summary>
    private void Restore(IPlayerCharacter me, IReadOnlyList<WantedMoodle> wanted, string when)
    {
        var onMe = OnMe(me);
        var back = new List<string>();

        foreach (var w in wanted)
        {
            if (onMe.ContainsKey(w.Guid)) continue;
            Apply(me, w);
            sent[w.Guid] = w;
            back.Add(w.Title);
        }

        if (config.ProtectOtherMoodles)
        {
            foreach (var m in othersSnapshot)
            {
                if (onMe.ContainsKey(m.GUID)) continue;
                // Applied as you, so Moodles' "party members" permission lets it through.
                var data = m with { Applier = ApplierName(me) };
                Plugin.PluginInterface.GetIpcSubscriber<MoodleData, nint, object>("Moodles.AddOrUpdateMoodleByDataByPtrV2")
                      .InvokeAction(data, me.Address);
                back.Add(Clean(m.Title));
            }
        }

        if (back.Count > 0)
        {
            LastChange = $"{Stamp()} Put back {string.Join(", ", back)} after Moodles dropped them ({when}).";
            lastApply = DateTime.UtcNow;
        }
    }

    /// <summary>Forget what was sent, so everything is applied again on the next tick.</summary>
    public void Reset()
    {
        sent.Clear();
        cleanedUp = false;
        nextCheck = DateTime.MinValue;
        retryAt = DateTime.MinValue;
        missingStreak = 0;
    }

    public void RemoveAll(IPlayerCharacter? me)
    {
        if (me == null) return;
        try
        {
            Remove(me, Needs.AllMoodleGuids.ToList());
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Couldn't remove Moodles on unload");
        }
        sent.Clear();
    }

    private static bool Loading() =>
        Plugin.Condition[ConditionFlag.BetweenAreas] ||
        Plugin.Condition[ConditionFlag.BetweenAreas51] ||
        Plugin.Condition[ConditionFlag.LoggingOut];

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss");

    private static string Clean(string title) =>
        System.Text.RegularExpressions.Regex.Replace(title ?? string.Empty, @"\[/?[a-zA-Z]+(=[^\]]*)?\]", string.Empty).Trim();

    private static string ApplierName(IPlayerCharacter me) =>
        $"{me.Name.TextValue}@{me.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty}";

    private static void Apply(IPlayerCharacter me, WantedMoodle w)
    {
        MoodleData data = (MoodleVersion, w.Guid, (int)w.Icon, w.Title, w.Description, string.Empty,
            -1L, w.Positive ? Positive : Negative, Math.Max(1, w.Stacks), 0, 0u, Guid.Empty, 0,
            ApplierName(me), string.Empty, false);
        Plugin.PluginInterface.GetIpcSubscriber<MoodleData, nint, object>("Moodles.AddOrUpdateMoodleByDataByPtrV2")
              .InvokeAction(data, me.Address);
    }

    private static void Remove(IPlayerCharacter me, List<Guid> guids)
    {
        if (guids.Count == 0) return;
        Plugin.PluginInterface.GetIpcSubscriber<List<Guid>, nint, object>("Moodles.RemoveMoodlesByPtrV2")
              .InvokeAction(guids, me.Address);
    }

    private static Dictionary<Guid, MoodleData> OnMe(IPlayerCharacter me)
    {
        var result = new Dictionary<Guid, MoodleData>();
        var list = Plugin.PluginInterface.GetIpcSubscriber<nint, List<MoodleData>>("Moodles.GetStatusManagerInfoByPtrV2")
                         .InvokeFunc(me.Address);
        foreach (var m in list ?? []) result[m.GUID] = m;
        return result;
    }

    private void Verify(IPlayerCharacter me, DateTime now)
    {
        MoodlesApiVersion ??= Plugin.PluginInterface.GetIpcSubscriber<int>("Moodles.Version").InvokeFunc();

        var onMe = OnMe(me);
        var missing = sent.Values.Where(w => !onMe.ContainsKey(w.Guid)).ToList();
        if (missing.Count == 0)
        {
            missingStreak = 0;
            Refused = false;
            Status = sent.Count == 0 ? "Connected to Moodles. No needs to show right now." : $"Connected to Moodles. Showing {sent.Count}.";
            return;
        }

        // Put them back right away (removed by hand, or dropped by Moodles).
        foreach (var w in missing) Apply(me, w);
        lastApply = now;

        // Only call it "blocked" when everything we send keeps vanishing, well clear of a zone change.
        missingStreak = missing.Count == sent.Count ? missingStreak + 1 : 0;
        if (missingStreak >= 3 && now >= settleUntil)
        {
            Refused = true;
            Status = "Moodles isn't accepting our Moodles. In Moodles' Settings, under \"Allow other plugins apply Moodles\", also tick \"Allow applying moodles from party members\" (or \"from everyone\"). Creature Comforts applies them as you, and you count as a member of your own party.";
            return;
        }

        if (!Refused)
        {
            Status = $"Put back {missing.Count} Moodle(s) that had gone missing.";
            LastChange = $"{Stamp()} Put back {string.Join(", ", missing.Select(w => w.Title))} (missing from Moodles).";
        }
    }
}

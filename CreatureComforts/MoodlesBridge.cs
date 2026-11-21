using System;
using System.Collections.Generic;
using System.Linq;
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
/// Moodles only accepts statuses from other plugins when its "Allow other plugins to apply Moodles"
/// setting is on AND it allows the applier (party/friends/all); we apply as the player themselves,
/// who counts as a party member, so "party" or "all" is enough.
/// </summary>
public sealed class MoodlesBridge
{
    private const int MoodleVersion = 2;
    private const int Positive = 0;
    private const int Negative = 1;

    /// <summary>What we last asked Moodles to show (guid -> moodle).</summary>
    private readonly Dictionary<Guid, WantedMoodle> sent = [];

    private DateTime nextCheck = DateTime.MinValue;
    private DateTime lastApply = DateTime.MinValue;
    private bool cleanedUp;
    private DateTime retryAt = DateTime.MinValue;

    // Zone changes: while loading (and for a few seconds after) the character is being rebuilt and Moodles may
    // not show its statuses yet. Touching anything then made Moodles fire its "applied" pop-up again.
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(8);
    private DateTime quietUntil = DateTime.MinValue;
    private nint lastAddress;
    private uint lastTerritory;

    /// <summary>Moodles seen missing on the previous check; only put back if still missing on the next one.</summary>
    private readonly HashSet<Guid> missingLastCheck = [];

    /// <summary>The last thing we changed in Moodles, and why (shown on the Moodles page).</summary>
    public string LastChange { get; private set; } = "Nothing yet.";

    public string Status { get; private set; } = "Not checked yet.";
    public bool Available { get; private set; }

    /// <summary>True when a Moodle we sent didn't show up: Moodles' settings are probably blocking it.</summary>
    public bool Refused { get; private set; }

    public int? MoodlesApiVersion { get; private set; }

    /// <summary>Called every tick with what should be showing. Sends only changes, and checks now and then.</summary>
    public void Sync(IPlayerCharacter me, IReadOnlyList<WantedMoodle> wanted)
    {
        var now0 = DateTime.UtcNow;
        if (now0 < retryAt) return;

        // Hold still through loading screens, zone changes and duty starts.
        if (Loading())
        {
            quietUntil = now0 + SettleTime;
            return;
        }
        var territory = Plugin.ClientState.TerritoryType;
        if (me.Address != lastAddress || territory != lastTerritory)
        {
            lastAddress = me.Address;
            lastTerritory = territory;
            quietUntil = now0 + SettleTime;
            missingLastCheck.Clear();
        }
        if (now0 < quietUntil) return;

        try
        {
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
                lastApply = DateTime.UtcNow;
            }

            // Every 15 s (and shortly after applying) make sure ours are really there; put back any that aren't
            // (removed by hand, or Moodles reloaded).
            var now = DateTime.UtcNow;
            if (now >= nextCheck && now - lastApply > TimeSpan.FromSeconds(1.5))
            {
                nextCheck = now + TimeSpan.FromSeconds(15);
                Verify(me);
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

    /// <summary>Forget what was sent, so everything is applied again on the next tick.</summary>
    public void Reset()
    {
        sent.Clear();
        cleanedUp = false;
        nextCheck = DateTime.MinValue;
        retryAt = DateTime.MinValue;
        missingLastCheck.Clear();
    }

    private static bool Loading() =>
        Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas] ||
        Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51] ||
        Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.LoggingOut];

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss");

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

    private void Verify(IPlayerCharacter me)
    {
        MoodlesApiVersion ??= Plugin.PluginInterface.GetIpcSubscriber<int>("Moodles.Version").InvokeFunc();

        var onMe = Plugin.PluginInterface.GetIpcSubscriber<nint, List<MoodleData>>("Moodles.GetStatusManagerInfoByPtrV2")
                         .InvokeFunc(me.Address)
                         .ToDictionary(m => m.GUID, m => m);

        var missing = sent.Values.Where(w => !onMe.ContainsKey(w.Guid)).ToList();
        if (missing.Count == 0)
        {
            missingLastCheck.Clear();
            Refused = false;
            Status = sent.Count == 0 ? "Connected to Moodles. No needs to show right now." : $"Connected to Moodles. Showing {sent.Count}.";
            return;
        }

        // Only act on Moodles that were missing last time too: a single miss can just be Moodles catching up.
        var confirmed = missing.Where(w => missingLastCheck.Contains(w.Guid)).ToList();
        missingLastCheck.Clear();
        foreach (var w in missing) missingLastCheck.Add(w.Guid);
        if (confirmed.Count == 0)
        {
            nextCheck = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            return;
        }

        var refusedNow = confirmed.Count == sent.Count;
        if (refusedNow && Refused)
        {
            // Still nothing after a retry: Moodles' settings are blocking us. Keep trying quietly.
            foreach (var w in confirmed) Apply(me, w);
            return;
        }

        Refused = refusedNow;
        Status = Refused
            ? "Moodles isn't accepting our Moodles. In Moodles' Settings, under \"Allow other plugins apply Moodles\", also tick \"Allow applying moodles from party members\" (or \"from everyone\"). Creature Comforts applies them as you, and you count as a member of your own party."
            : $"Put back {confirmed.Count} Moodle(s) that had gone missing.";
        if (!Refused)
            LastChange = $"{Stamp()} Put back {string.Join(", ", confirmed.Select(w => w.Title))} (missing from Moodles on two checks).";
        foreach (var w in confirmed) Apply(me, w);
        lastApply = DateTime.UtcNow;
    }
}

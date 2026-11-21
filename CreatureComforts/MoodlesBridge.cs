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

    public string Status { get; private set; } = "Not checked yet.";
    public bool Available { get; private set; }

    /// <summary>True when a Moodle we sent didn't show up: Moodles' settings are probably blocking it.</summary>
    public bool Refused { get; private set; }

    public int? MoodlesApiVersion { get; private set; }

    /// <summary>Called every tick with what should be showing. Sends only changes, and checks now and then.</summary>
    public void Sync(IPlayerCharacter me, IReadOnlyList<WantedMoodle> wanted)
    {
        if (DateTime.UtcNow < retryAt) return;
        try
        {
            if (!cleanedUp)
            {
                // Start clean: remove anything we left on the character last time.
                Remove(me, Needs.AllMoodleGuids.ToList());
                sent.Clear();
                cleanedUp = true;
            }

            var wantedIds = wanted.Select(w => w.Guid).ToHashSet();
            var stale = sent.Keys.Where(g => !wantedIds.Contains(g)).ToList();
            if (stale.Count > 0)
            {
                Remove(me, stale);
                foreach (var g in stale) sent.Remove(g);
            }

            foreach (var w in wanted)
            {
                if (sent.TryGetValue(w.Guid, out var old) && old == w) continue;
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
            Refused = false;
            Status = sent.Count == 0 ? "Connected to Moodles. No needs to show right now." : $"Connected to Moodles. Showing {sent.Count}.";
            return;
        }

        if (missing.Count == sent.Count && Refused)
        {
            // Still nothing after a retry: Moodles' settings are blocking us. Keep trying quietly.
            foreach (var w in missing) Apply(me, w);
            return;
        }

        Refused = missing.Count == sent.Count;
        Status = Refused
            ? "Moodles isn't accepting our Moodles. In Moodles' Settings, turn on \"Allow other plugins to apply Moodles\" and allow party members (or everyone)."
            : $"Put back {missing.Count} Moodle(s) that had gone missing.";
        foreach (var w in missing) Apply(me, w);
        lastApply = DateTime.UtcNow;
    }
}

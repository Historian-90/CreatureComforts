using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Ipc;

namespace CreatureComforts;

/// <summary>
/// Lets other plugins read and nudge your needs. Need names are "Hunger", "Sleep", "Fun", "Social",
/// "Bladder" and "Hygiene"; values are 0-100 (100 = fully satisfied); levels are 0 (fine) to 3 (empty).
///
///   CreatureComforts.ApiVersion      () -> int
///   CreatureComforts.GetNeeds        () -> Dictionary&lt;string, float&gt;   (active needs only)
///   CreatureComforts.GetLevels       () -> Dictionary&lt;string, int&gt;
///   CreatureComforts.AddToNeed       (string need, float amount)      (negative drains)
///   CreatureComforts.LevelChanged    event (string need, int level)
/// </summary>
public sealed class Ipc : IDisposable
{
    public const int ApiVersion = 1;

    private readonly ICallGateProvider<int> apiVersion;
    private readonly ICallGateProvider<Dictionary<string, float>> getNeeds;
    private readonly ICallGateProvider<Dictionary<string, int>> getLevels;
    private readonly ICallGateProvider<string, float, object> addToNeed;
    private readonly ICallGateProvider<string, int, object> levelChanged;
    private readonly NeedTracker tracker;

    public Ipc(NeedTracker tracker)
    {
        this.tracker = tracker;
        var pi = Plugin.PluginInterface;

        apiVersion = pi.GetIpcProvider<int>("CreatureComforts.ApiVersion");
        apiVersion.RegisterFunc(() => ApiVersion);

        getNeeds = pi.GetIpcProvider<Dictionary<string, float>>("CreatureComforts.GetNeeds");
        getNeeds.RegisterFunc(() => tracker.Current == null
            ? []
            : tracker.ActiveNeeds.ToDictionary(k => k.ToString(), k => tracker.Current.Get(k)));

        getLevels = pi.GetIpcProvider<Dictionary<string, int>>("CreatureComforts.GetLevels");
        getLevels.RegisterFunc(() => tracker.ActiveNeeds.ToDictionary(k => k.ToString(), tracker.Level));

        addToNeed = pi.GetIpcProvider<string, float, object>("CreatureComforts.AddToNeed");
        addToNeed.RegisterAction((name, amount) =>
        {
            if (Enum.TryParse<NeedKind>(name, true, out var kind)) tracker.Add(kind, amount);
        });

        levelChanged = pi.GetIpcProvider<string, int, object>("CreatureComforts.LevelChanged");
        tracker.LevelChanged += OnLevelChanged;
    }

    private void OnLevelChanged(NeedKind kind, int level) => levelChanged.SendMessage(kind.ToString(), level);

    public void Dispose()
    {
        tracker.LevelChanged -= OnLevelChanged;
        apiVersion.UnregisterFunc();
        getNeeds.UnregisterFunc();
        getLevels.UnregisterFunc();
        addToNeed.UnregisterAction();
    }
}

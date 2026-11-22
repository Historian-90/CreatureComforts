using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using CreatureComforts.Windows;

namespace CreatureComforts;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    private const string Command = "/comforts";
    private const string ShortCommand = "/needs";

    public Configuration Configuration { get; }
    public NeedTracker Tracker { get; }
    public MoodlesBridge Moodles { get; }

    private readonly Ipc ipc;
    private readonly WindowSystem windows = new("CreatureComforts");
    private readonly MainWindow mainWindow;
    private readonly HudWindow hudWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        foreach (var n in Needs.All) Configuration.For(n.Kind);
        Configuration.Migrate();
        Moodles = new MoodlesBridge(Configuration);

        Tracker = new NeedTracker(this);
        ipc = new Ipc(Tracker);

        mainWindow = new MainWindow(this);
        windows.AddWindow(mainWindow);
        hudWindow = new HudWindow(this);
        windows.AddWindow(hudWindow);

        var help = new CommandInfo(OnCommand) { HelpMessage = "Open Creature Comforts. \"/comforts hud\" shows or hides the mini needs display; \"/comforts lock\" locks or unlocks it." };
        CommandManager.AddHandler(Command, help);
        CommandManager.AddHandler(ShortCommand, new CommandInfo(OnCommand) { HelpMessage = "Same as /comforts." });

        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMain;
        Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMain;
        CommandManager.RemoveHandler(Command);
        CommandManager.RemoveHandler(ShortCommand);
        windows.RemoveAllWindows();
        mainWindow.Dispose();
        ipc.Dispose();
        Moodles.RemoveAll(ObjectTable.LocalPlayer);
        Tracker.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "hud":
                Configuration.ShowHud = !Configuration.ShowHud;
                Configuration.Save();
                break;
            case "lock":
                Configuration.HudLocked = !Configuration.HudLocked;
                Configuration.Save();
                break;
            default:
                ToggleMain();
                break;
        }
    }

    public void ToggleMain() => mainWindow.Toggle();

    public void ResetHudPosition() => hudWindow.ResetPosition();

    private void OnUpdate(IFramework framework)
    {
        try
        {
            var me = ObjectTable.LocalPlayer;
            Tracker.Tick(me);
            if (me != null && Tracker.Current != null)
                Moodles.Sync(me, Tracker.WantedMoodles());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update failed");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace CreatureComforts.Windows;

/// <summary>
/// Creature Comforts' window, laid out like Outer Voice and EasyIsle: a header, an icon rail on the
/// left and one page at a time on the right.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 Muted = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.75f, 0.35f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Good = new(0.55f, 0.9f, 0.55f, 1f);
    private static readonly Vector4 Accent = new(0.55f, 0.42f, 0.85f, 1f);
    private const float RailWidth = 46f;

    private enum Page { Needs, Moodles, Places, Testing }

    private readonly Plugin plugin;
    private Page page = Page.Needs;
    private string? pageError;

    // Pickers
    private List<(uint Id, string Name, string Command)>? emotes;
    private string emoteFilter = string.Empty;
    private string iconFilter = string.Empty;
    private bool iconStackableOnly = true;
    private NeedKind? iconTarget;
    private bool iconForSerene;
    private string newDance = string.Empty;

    private Configuration Config => plugin.Configuration;
    private NeedTracker Tracker => plugin.Tracker;

    public MainWindow(Plugin plugin) : base("Creature Comforts###CreatureComfortsMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(620, 420) };
        Size = new Vector2(780, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void OnClose() => Tracker.Save();

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();

        ImGui.BeginChild("##rail", new Vector2(RailWidth, 0));
        RailButton(Page.Needs, FontAwesomeIcon.Heart, "Needs\nHow your character is doing, and how each need drains and refills.");
        RailButton(Page.Moodles, FontAwesomeIcon.TheaterMasks, "Moodles\nHow your needs look as Moodles: icons, names, when they appear.");
        RailButton(Page.Places, FontAwesomeIcon.Home, "Places\nRest spots and venues.");
        RailButton(Page.Testing, FontAwesomeIcon.Flask, "Testing\nWhat the plugin sees right now, and sliders to try things out.");
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##page", Vector2.Zero);
        try
        {
            switch (page)
            {
                case Page.Needs: DrawNeeds(); break;
                case Page.Moodles: DrawMoodles(); break;
                case Page.Places: DrawPlaces(); break;
                case Page.Testing: DrawTesting(); break;
            }
            pageError = null;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"{page} page failed");
            pageError = ex.ToString();
        }

        if (pageError != null)
        {
            Colored(Bad, $"Something went wrong drawing the {page} page.");
            if (ImGui.Button("Copy error")) ImGui.SetClipboardText(pageError);
        }
        ImGui.EndChild();

        DrawIconPicker();
    }

    private void RailButton(Page target, FontAwesomeIcon icon, string tooltip)
    {
        var active = page == target;
        var size = new Vector2(RailWidth - 8, RailWidth - 8);
        var clicked = active
            ? ImGuiComponents.IconButton((int)target, icon, Accent, Accent, Accent, size)
            : ImGuiComponents.IconButton((int)target, icon, new Vector4(0, 0, 0, 0), null, null, size);
        if (clicked) page = target;
        if (ImGui.IsItemHovered()) Tip(tooltip);
        ImGui.Spacing();
    }

    private void DrawHeader()
    {
        var enabled = Config.Enabled;
        if (ImGui.Checkbox("Needs on", ref enabled))
        {
            Config.Enabled = enabled;
            Config.Save();
        }
        if (ImGui.IsItemHovered()) Tip("Turn off to pause every need and take the Moodles off.");

        ImGui.SameLine();
        var hardcore = Config.Hardcore;
        if (ImGui.Checkbox("Hardcore mode", ref hardcore))
        {
            Config.Hardcore = hardcore;
            Config.Save();
        }
        if (ImGui.IsItemHovered()) Tip("Adds Bladder and Hygiene, each refilled by an emote you choose.");

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        var m = plugin.Moodles;
        if (!m.Available || m.Refused)
            Colored(Warn, m.Available ? "Moodles is blocking us (see Moodles page)" : "Moodles not found");
        else
            Colored(Good, "Moodles connected");
    }

    // ---- Needs ----------------------------------------------------------------------------------

    private void DrawNeeds()
    {
        var c = Tracker.Current;
        if (c == null)
        {
            Note("Log in to see your character's needs.");
            return;
        }

        Title($"{c.Name}");
        Note("Needs drain in real time while you're logged in, and pause while you're logged out.");
        ImGui.Spacing();

        foreach (var info in Needs.All)
        {
            if (info.Hardcore && !Config.Hardcore) continue;
            DrawNeedRow(info, c);
        }

        if (!Config.Hardcore)
        {
            ImGui.Spacing();
            Note("Turn on Hardcore mode (top) for Bladder and Hygiene.");
        }
    }

    private void DrawNeedRow(NeedInfo info, CharacterNeeds c)
    {
        var s = Config.For(info.Kind);
        ImGui.PushID(info.Name);

        Icon(s.Icon, 32, Math.Max(1, Tracker.Level(info.Kind)));
        ImGui.SameLine();
        ImGui.BeginGroup();

        var on = s.Enabled;
        if (ImGui.Checkbox($"##on", ref on))
        {
            s.Enabled = on;
            Config.Save();
        }
        if (ImGui.IsItemHovered()) Tip(on ? "Tracking this need. Untick to ignore it." : "Not tracked.");
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(info.Name);

        if (s.Enabled)
        {
            var value = c.Get(info.Kind);
            var level = Tracker.Level(info.Kind);
            ImGui.SameLine(150);
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, BarColor(value));
            ImGui.ProgressBar(value / 100f, new Vector2(200, 0), $"{value:0}%");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            if (level == 0) Colored(Muted, "fine");
            else Colored(level >= 3 ? Bad : Warn, s.TitleFor(level, info.Kind));

            ImGui.SetNextItemWidth(200);
            var hours = s.HoursToEmpty;
            if (ImGui.SliderFloat("Full to empty##hours", ref hours, 0.5f, 24f, FormatHours(hours)))
                s.HoursToEmpty = MathF.Round(hours * 4) / 4;
            if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
            if (ImGui.IsItemHovered()) Tip("Real time for this need to run from full to empty. Ctrl+click to type.");

            switch (info.Kind)
            {
                case NeedKind.Sleep:
                    ImGui.SameLine();
                    SliderSetting("Faster in duties##duty", Config.SleepDutyMultiplier, 1f, 6f, "x%.1f", v => Config.SleepDutyMultiplier = v, 110);
                    SliderSetting("Bed: empty to full##bed", Config.BedRefillMinutes, 1f, 60f, "%.0f min", v => Config.BedRefillMinutes = v, 200);
                    ImGui.SameLine();
                    SliderSetting("Logged out resting##off", Config.OfflineRefillMinutes, 5f, 480f, "%.0f min", v => Config.OfflineRefillMinutes = v, 110);
                    break;
                case NeedKind.Fun:
                    SliderSetting("Per MGP win##mgp", Config.FunPerMgpWin, 0f, 50f, "+%.0f", v => Config.FunPerMgpWin = v, 200);
                    ImGui.SameLine();
                    SliderSetting("Per dance##dance", Config.FunPerDance, 0f, 25f, "+%.0f", v => Config.FunPerDance = v, 110);
                    SliderSetting("Venue: empty to full##venue", Config.VenueRefillMinutes, 5f, 180f, "%.0f min", v => Config.VenueRefillMinutes = v, 200);
                    ImGui.SameLine();
                    SliderSetting("Toy Chest##toy", Config.FunPerToyChest, 0f, 50f, "+%.0f", v => Config.FunPerToyChest = v, 110);
                    DrawDanceList();
                    break;
                case NeedKind.Social:
                    SliderSetting("Party: empty to full##party", Config.PartyRefillMinutes, 5f, 180f, "%.0f min", v => Config.PartyRefillMinutes = v, 200);
                    ImGui.SameLine();
                    SliderSetting("Per chat##chat", Config.SocialPerChat, 0f, 25f, "+%.0f", v => Config.SocialPerChat = v, 110);
                    SliderSetting("Per emote at someone##emote", Config.SocialPerEmote, 0f, 25f, "+%.0f", v => Config.SocialPerEmote = v, 200);
                    break;
                case NeedKind.Bladder:
                case NeedKind.Hygiene:
                    DrawEmotePicker(s);
                    ImGui.SameLine();
                    SliderSetting("Fill while holding##hold", Config.HoldRefillPerSecond, 1f, 50f, "%.0f%%/s", v => Config.HoldRefillPerSecond = v, 110);
                    DrawPosePicker(s);
                    if (Tracker.Holding(info.Kind)) Colored(Good, "Filling while you hold it...");
                    if (info.Kind == NeedKind.Hygiene)
                    {
                        ImGui.SameLine();
                        SliderSetting("Faster in combat##combat", Config.HygieneCombatMultiplier, 1f, 6f, "x%.1f", v => Config.HygieneCombatMultiplier = v, 110);
                    }
                    break;
            }
            Colored(Muted, info.HowToFill);
        }

        ImGui.EndGroup();
        ImGui.Separator();
        ImGui.PopID();
    }

    private void SliderSetting(string label, float value, float min, float max, string format, Action<float> set, float width)
    {
        ImGui.SetNextItemWidth(width);
        if (ImGui.SliderFloat(label, ref value, min, max, format)) set(value);
        if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
    }

    private void DrawEmotePicker(NeedSettings s)
    {
        emotes ??= GameInfo.PickableEmotes();
        var current = s.RefillEmote == 0 ? "Pick an emote..." : GameInfo.EmoteName(s.RefillEmote);
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("Refill emote##emote", current, ImGuiComboFlags.HeightLarge))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##emotefilter", "Search...", ref emoteFilter, 64);
            if (ImGui.Selectable("(none)", s.RefillEmote == 0))
            {
                s.RefillEmote = 0;
                Config.Save();
            }
            foreach (var e in emotes)
            {
                if (emoteFilter.Length > 0 &&
                    !e.Name.Contains(emoteFilter, StringComparison.OrdinalIgnoreCase) &&
                    !e.Command.Contains(emoteFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (ImGui.Selectable($"{e.Name}  {e.Command}##e{e.Id}", s.RefillEmote == e.Id))
                {
                    s.RefillEmote = e.Id;
                    Config.Save();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            Tip("Holding this emote fills the need a little every second. Pick an emote that loops (keeps playing until you move), or it only counts for the moment it plays. Pair it with a Penumbra animation mod that replaces the emote.");
    }

    private void DrawPosePicker(NeedSettings s)
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        if (ImGui.Button("Use my current pose##pose") && me != null)
        {
            var p = GameInfo.CurrentPose(me);
            s.RefillPoseType = p.Type;
            s.RefillPoseIndex = p.Index;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            Tip("Get into the idle pose your animation mod replaces (stand or /groundsit, then /cpose until it's the right one), then click this. Holding that pose while standing still fills the need a little every second.");
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        if (s.RefillPoseType < 0)
        {
            Colored(Muted, "Refill pose: none");
            return;
        }
        ImGui.TextWrapped("Refill pose: " + GameInfo.PoseName(s.RefillPoseType, s.RefillPoseIndex));
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear##pose"))
        {
            s.RefillPoseType = -1;
            Config.Save();
        }
    }

    private void DrawDanceList()
    {
        if (!ImGui.TreeNode("Emotes that count as dancing##dances")) return;
        Note("Any emote with \"dance\" in its name counts. Add others here (English emote names).");
        string? remove = null;
        foreach (var d in Config.ExtraDanceEmotes)
        {
            ImGui.PushID(d);
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Times)) remove = d;
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(d);
            ImGui.PopID();
        }
        if (remove != null)
        {
            Config.ExtraDanceEmotes.Remove(remove);
            Config.Save();
        }
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##newdance", "Emote name", ref newDance, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add") && newDance.Trim().Length > 0)
        {
            Config.ExtraDanceEmotes.Add(newDance.Trim());
            newDance = string.Empty;
            Config.Save();
        }
        ImGui.TreePop();
    }

    // ---- Moodles --------------------------------------------------------------------------------

    private void DrawMoodles()
    {
        var m = plugin.Moodles;
        Title("Moodles");
        if (!m.Available) Colored(Warn, m.Status);
        else if (m.Refused) Colored(Bad, m.Status);
        else Colored(Good, m.Status);
        Colored(Muted, "Last change: " + m.LastChange);
        if (ImGui.Button("Apply again"))
            m.Reset();
        if (ImGui.IsItemHovered()) Tip("Take Creature Comforts' Moodles off and put the right ones back on.");

        var protect = Config.ProtectOtherMoodles;
        if (ImGui.Checkbox("Keep my other Moodles through zone changes", ref protect))
        {
            Config.ProtectOtherMoodles = protect;
            Config.Save();
        }
        if (ImGui.IsItemHovered())
            Tip("Once another plugin has given you a Moodle, Moodles drops ALL your Moodles whenever you change zones (it treats you like a synced friend who walked away). Creature Comforts puts its own back either way; with this on it also puts back the other Moodles you had. Timed Moodles start their timer over.");

        ImGui.Spacing();
        Title("When Moodles appear");
        Note("Each need shows one Moodle once it drops below the first line, with more stacks as it drops further.");
        var t1 = Config.Threshold1; var t2 = Config.Threshold2; var t3 = Config.Threshold3;
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat("1 stack below##t1", ref t1, 5, 95, "%.0f%%")) Config.Threshold1 = Math.Max(t1, Config.Threshold2 + 1);
        if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat("2 stacks below##t2", ref t2, 3, 90, "%.0f%%")) Config.Threshold2 = Math.Clamp(t2, Config.Threshold3 + 1, Config.Threshold1 - 1);
        if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
        ImGui.SetNextItemWidth(220);
        if (ImGui.SliderFloat("3 stacks below##t3", ref t3, 1, 85, "%.0f%%")) Config.Threshold3 = Math.Min(t3, Config.Threshold2 - 1);
        if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();

        ImGui.Spacing();
        Title("Looks");
        Note("Click an icon to change it. Icons marked \"stacks\" show 2 and 3 on the status bar; others always look the same.");
        foreach (var info in Needs.All)
        {
            if (info.Hardcore && !Config.Hardcore) continue;
            var s = Config.For(info.Kind);
            ImGui.PushID("look" + info.Name);
            if (IconButton(s.Icon, 32)) { iconTarget = info.Kind; iconForSerene = false; ImGui.OpenPopup("##iconpicker"); }
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextUnformatted(info.Name);
            ImGui.SameLine();
            Colored(Muted, IconStacks.MaxStacks(s.Icon) >= 3 ? "(stacks)" : "(no stack icons)");
            while (s.Titles.Count < 3) s.Titles.Add(info.DefaultTitles[s.Titles.Count]);
            for (var i = 0; i < 3; i++)
            {
                var title = s.Titles[i];
                ImGui.SetNextItemWidth(150);
                if (ImGui.InputText($"##title{i}", ref title, 64)) s.Titles[i] = title;
                if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
                if (ImGui.IsItemHovered()) Tip($"Moodle name at {i + 1} stack{(i == 0 ? "" : "s")}.");
                if (i < 2) ImGui.SameLine();
            }
            var desc = s.Description;
            ImGui.SetNextItemWidth(470);
            if (ImGui.InputText("##desc", ref desc, 256)) s.Description = desc;
            if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
            ImGui.EndGroup();
            ImGui.PopID();
            ImGui.Spacing();
        }

        ImGui.Separator();
        ImGui.PushID("serene");
        if (IconButton(Config.SereneIcon, 32)) { iconTarget = null; iconForSerene = true; ImGui.OpenPopup("##iconpicker"); }
        ImGui.SameLine();
        ImGui.BeginGroup();
        var serene = Config.ShowSerene;
        if (ImGui.Checkbox("Show a buff when every need is met", ref serene))
        {
            Config.ShowSerene = serene;
            Config.Save();
        }
        var st = Config.SereneTitle;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputText("##stitle", ref st, 64)) Config.SereneTitle = st;
        if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
        ImGui.SameLine();
        var sd = Config.SereneDescription;
        ImGui.SetNextItemWidth(310);
        if (ImGui.InputText("##sdesc", ref sd, 256)) Config.SereneDescription = sd;
        if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
        ImGui.EndGroup();
        ImGui.PopID();

        ImGui.Spacing();
        Note("Outer Voice tip: make a custom event with a Moodle condition (for example \"Starving\") and your character will say something when it appears.");
    }

    private void DrawIconPicker()
    {
        ImGui.SetNextWindowSize(new Vector2(560, 460), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("##iconpicker")) return;

        ImGui.SetNextItemWidth(240);
        ImGui.InputTextWithHint("##iconfilter", "Search status names...", ref iconFilter, 64);
        ImGui.SameLine();
        ImGui.Checkbox("Only icons with stacks", ref iconStackableOnly);

        ImGui.BeginChild("##icons", new Vector2(0, -ImGui.GetFrameHeightWithSpacing()));
        var perRow = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / 40));
        var n = 0;
        foreach (var icon in IconStacks.All)
        {
            if (iconStackableOnly && icon.MaxStacks < 3) continue;
            if (iconFilter.Length > 0 && !icon.Name.Contains(iconFilter, StringComparison.OrdinalIgnoreCase)) continue;
            if (n++ % perRow != 0) ImGui.SameLine();
            ImGui.PushID((int)icon.Icon);
            if (IconButton(icon.Icon, 32))
            {
                if (iconForSerene) Config.SereneIcon = icon.Icon;
                else if (iconTarget is { } k) Config.For(k).Icon = icon.Icon;
                Config.Save();
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered())
                Tip($"{icon.Name}\nicon {icon.Icon}{(icon.MaxStacks > 1 ? $", up to {icon.MaxStacks} stacks" : "")}");
            ImGui.PopID();
        }
        ImGui.EndChild();
        if (ImGui.Button("Close")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    // ---- Places ---------------------------------------------------------------------------------

    private void DrawPlaces()
    {
        Title("Rest spots");
        Note("Logging out in a rest spot counts as sleep: your Sleep need fills up while you're away.");
        Note("Rest spots: inn rooms, and your own house, FC house, private chambers or apartment.");
        var any = Config.AnyHouseIsRestSpot;
        if (ImGui.Checkbox("Any house interior counts (friends' homes too)", ref any))
        {
            Config.AnyHouseIsRestSpot = any;
            Config.Save();
        }
        ImGui.AlignTextToFramePadding();
        ImGui.TextWrapped("Here: " + (Plugin.ClientState.IsLoggedIn ? GameInfo.Describe() : "not logged in"));
        ImGui.SameLine();
        if (Tracker.RestSpot) Colored(Good, "(rest spot)"); else Colored(Muted, "(not a rest spot)");

        ImGui.Spacing();
        Title("Venues");
        Note("Time inside a venue fills your Fun need. Register a venue while you're inside it.");

        var here = GameInfo.CurrentHouse();
        var venueHere = GameInfo.VenueHere(Config);
        if (here is { } h && venueHere == null)
        {
            if (ImGui.Button("Register this place as a venue"))
            {
                Config.Venues.Add(new Venue { HouseId = h.Id, Name = GameInfo.HouseName(h) });
                Config.Save();
            }
        }
        else if (venueHere != null)
        {
            Colored(Good, $"You're at {venueHere.Name}.");
        }
        else
        {
            Colored(Muted, "Go inside a house to register it.");
        }

        Venue? delete = null;
        foreach (var v in Config.Venues)
        {
            ImGui.PushID(v.HouseId.ToString());
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash) && ImGui.GetIO().KeyCtrl) delete = v;
            if (ImGui.IsItemHovered()) Tip("Ctrl+click to remove.");
            ImGui.SameLine();
            var name = v.Name;
            ImGui.SetNextItemWidth(320);
            if (ImGui.InputText("##vname", ref name, 64)) v.Name = name;
            if (ImGui.IsItemDeactivatedAfterEdit()) Config.Save();
            ImGui.SameLine();
            Colored(Muted, GameInfo.HouseName((FFXIVClientStructs.FFXIV.Client.Game.HouseId)v.HouseId));
            ImGui.PopID();
        }
        if (delete != null)
        {
            Config.Venues.Remove(delete);
            Config.Save();
        }
    }

    // ---- Testing --------------------------------------------------------------------------------

    private void DrawTesting()
    {
        Title("What the plugin sees");
        var me = Plugin.ObjectTable.LocalPlayer;
        if (me == null || Tracker.Current == null)
        {
            Note("Log in first.");
            return;
        }

        var pose = GameInfo.Pose(me);
        Line("Pose", $"{pose.Mode} / {pose.Param}  (lying in bed = InPositionLoop / {GameInfo.BedMode})");
        Line("Sleeping in bed", Tracker.Sleeping ? "yes" : "no");
        var cp = GameInfo.CurrentPose(me);
        Line("Idle pose (/cpose)", GameInfo.PoseName(cp.Type, cp.Index));
        Line("Emote now", $"{GameInfo.EmoteName(pose.Emote)} ({pose.Emote})");
        Line("Last emote seen", Tracker.LastEmoteSeen);
        Line("Dancing", Tracker.Dancing ? "yes" : "no");
        var fed = GameInfo.WellFedLeft(me);
        Line("Well Fed", fed > TimeSpan.Zero ? $"yes, {fed:mm\\:ss} left" : "no");
        Line("MGP", GameInfo.Mgp().ToString("N0"));
        Line("In a party", Tracker.InParty ? "yes" : "no");
        Line("In a duty", GameInfo.InDuty() ? "yes" : "no");
        Line("At a venue", Tracker.AtVenue ? "yes" : "no");
        Line("Place", GameInfo.Describe());
        Line("Last activity", Tracker.LastActivity);

        ImGui.Spacing();
        Title("Try it out");
        Note("Drag a slider to set a need directly, to check how the Moodles look.");
        foreach (var kind in Tracker.ActiveNeeds)
        {
            var v = Tracker.Current.Get(kind);
            ImGui.SetNextItemWidth(260);
            if (ImGui.SliderFloat($"{Needs.Info(kind).Name}##set", ref v, 0, 100, "%.0f%%"))
                Tracker.SetValue(kind, v);
        }
        if (ImGui.Button("Fill all"))
            foreach (var kind in Tracker.ActiveNeeds) Tracker.SetValue(kind, 100);
        ImGui.SameLine();
        if (ImGui.Button("Empty all"))
            foreach (var kind in Tracker.ActiveNeeds) Tracker.SetValue(kind, 0);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static string FormatHours(float h) => h < 1 ? $"{h * 60:0} min" : h % 1 == 0 ? $"{h:0} h" : $"{h:0.##} h";

    private Vector4 BarColor(float value) =>
        value < Config.Threshold3 ? new Vector4(0.85f, 0.3f, 0.3f, 1f)
        : value < Config.Threshold2 ? new Vector4(0.9f, 0.55f, 0.2f, 1f)
        : value < Config.Threshold1 ? new Vector4(0.85f, 0.8f, 0.3f, 1f)
        : new Vector4(0.4f, 0.75f, 0.4f, 1f);

    /// <summary>A status icon; for stackable icons, stack 2-3 use the game's numbered variants.</summary>
    private static void Icon(uint icon, float size, int stacks = 1)
    {
        var id = IconStacks.MaxStacks(icon) >= stacks ? icon + (uint)Math.Max(0, stacks - 1) : icon;
        var tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(id)).GetWrapOrEmpty();
        ImGui.Image(tex.Handle, new Vector2(size * 0.75f, size));
    }

    private static bool IconButton(uint icon, float size)
    {
        var tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
        return ImGui.ImageButton(tex.Handle, new Vector2(size * 0.75f, size));
    }

    private static void Title(string text)
    {
        ImGui.SetWindowFontScale(1.15f);
        ImGui.TextWrapped(text);
        ImGui.SetWindowFontScale(1f);
    }

    private static void Line(string label, string value)
    {
        Colored(Muted, label + ":");
        ImGui.SameLine(160);
        ImGui.TextWrapped(value);
    }

    private static void Note(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    /// <summary>A hover tooltip that wraps long text instead of stretching across the screen.</summary>
    private static void Tip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    /// <summary>Colored text that wraps at the edge of the page instead of running off it.</summary>
    private static void Colored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }
}

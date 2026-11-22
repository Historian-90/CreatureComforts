using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;

namespace CreatureComforts.Windows;

/// <summary>
/// The small always-on needs display: one icon and bar per need, no text. Drag it where you like while it's
/// unlocked; locked, it can't be moved and clicks go straight through to the game.
/// </summary>
public sealed class HudWindow : Window
{
    private readonly Plugin plugin;
    private Vector2? lastSavedPos;

    private Configuration Config => plugin.Configuration;

    public HudWindow(Plugin plugin) : base("Creature Comforts needs###CreatureComfortsHud")
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        IsOpen = true;
        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;
    }

    /// <summary>Jump to the saved position on the next draw.</summary>
    public void ResetPosition() => lastSavedPos = null;

    public override bool DrawConditions() =>
        Config.ShowHud && Config.Enabled && plugin.Tracker.Current != null && plugin.Tracker.ActiveNeeds.Any();

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoDocking;
        if (Config.HudLocked) Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoMouseInputs;

        // Where it was left last time (first draw only; after that the player drags it).
        if (lastSavedPos == null && Config.HudPosition is { } p)
        {
            Position = p;
            PositionCondition = ImGuiCond.Always;
        }
        else
        {
            Position = null;
        }

        BgAlpha = Config.HudBackground;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6) * Config.HudScale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5, 4) * Config.HudScale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
    }

    public override void PostDraw() => ImGui.PopStyleVar(3);

    public override void Draw()
    {
        var tracker = plugin.Tracker;
        var c = tracker.Current;
        if (c == null) return;

        var scale = Config.HudScale;
        var iconH = 28f * scale;
        var iconSize = new Vector2(iconH * 0.75f, iconH);
        var first = true;

        foreach (var kind in tracker.ActiveNeeds)
        {
            var info = Needs.Info(kind);
            var s = Config.For(kind);
            var value = c.Get(kind);
            var level = tracker.Level(kind);

            if (!first && Config.HudHorizontal) ImGui.SameLine();
            first = false;

            ImGui.BeginGroup();
            Icon(s.Icon, iconSize, Math.Max(1, level));
            if (Config.HudHorizontal)
            {
                Bar(value, new Vector2(iconSize.X, 5 * scale), tracker.Holding(kind));
            }
            else
            {
                ImGui.SameLine();
                var y = ImGui.GetCursorPosY();
                ImGui.SetCursorPosY(y + (iconH - 8 * scale) / 2);
                Bar(value, new Vector2(Config.HudBarWidth * scale, 8 * scale), tracker.Holding(kind));
            }
            ImGui.EndGroup();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{info.Name}: {value:0}%{(level > 0 ? $" ({s.TitleFor(level, kind)})" : "")}");
        }

        // Unlocked: right-click for options, and remember where it was dragged.
        if (!Config.HudLocked)
        {
            if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) ImGui.OpenPopup("##hudmenu");
            if (ImGui.BeginPopup("##hudmenu"))
            {
                if (ImGui.MenuItem("Lock in place")) { Config.HudLocked = true; Config.Save(); }
                if (ImGui.MenuItem(Config.HudHorizontal ? "Show as a list" : "Show as a row"))
                {
                    Config.HudHorizontal = !Config.HudHorizontal;
                    Config.Save();
                }
                if (ImGui.MenuItem("Open Creature Comforts")) plugin.ToggleMain();
                if (ImGui.MenuItem("Hide")) { Config.ShowHud = false; Config.Save(); }
                ImGui.EndPopup();
            }

            var pos = ImGui.GetWindowPos();
            if (lastSavedPos == null) lastSavedPos = pos;
            if (Vector2.Distance(pos, lastSavedPos.Value) > 0.5f && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                lastSavedPos = pos;
                Config.HudPosition = pos;
                Config.Save();
            }
        }
        else
        {
            lastSavedPos ??= ImGui.GetWindowPos();
        }
    }

    private void Bar(float value, Vector2 size, bool filling)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        var rounding = size.Y / 2;
        dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.55f)), rounding);
        var fill = Math.Clamp(value / 100f, 0f, 1f);
        if (fill > 0)
        {
            var color = BarColor(value);
            if (filling) color = Vector4.Lerp(color, Vector4.One, 0.35f + 0.25f * MathF.Sin((float)ImGui.GetTime() * 6f));
            dl.AddRectFilled(min, new Vector2(min.X + size.X * fill, max.Y), ImGui.GetColorU32(color), rounding);
        }
        dl.AddRect(min, max, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.15f)), rounding);
        ImGui.Dummy(size);
    }

    private Vector4 BarColor(float value) =>
        value < Config.Threshold3 ? new Vector4(0.85f, 0.3f, 0.3f, 1f)
        : value < Config.Threshold2 ? new Vector4(0.9f, 0.55f, 0.2f, 1f)
        : value < Config.Threshold1 ? new Vector4(0.85f, 0.8f, 0.3f, 1f)
        : new Vector4(0.4f, 0.75f, 0.4f, 1f);

    private static void Icon(uint icon, Vector2 size, int stacks)
    {
        var id = IconStacks.MaxStacks(icon) >= stacks ? icon + (uint)Math.Max(0, stacks - 1) : icon;
        var tex = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(id)).GetWrapOrEmpty();
        ImGui.Image(tex.Handle, size);
    }
}

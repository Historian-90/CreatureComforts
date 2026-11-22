# Creature Comforts

Sims-style needs for your FFXIV character, made for roleplay. Your character gets hungry, sleepy, bored
and lonely over time, and the things you already do in the game take care of it. Needs show up as
[Moodles](https://github.com/kawaii/Moodles) on your character, so friends who sync Moodles can see
how you're doing.

Flavor only: nothing here changes gameplay. Everything runs on your own PC; nothing is sent anywhere.

## Needs

| Need | Fills up when you... |
|---|---|
| **Hunger** | Eat or drink anything that gives *Well Fed*. You stay full until it wears off. |
| **Sleep** | Lie down with `/doze` on a bed, or log out in an inn room or your own home. Drains faster in duties. |
| **Fun** | Win MGP (GATEs, Triple Triad, Chocobo Racing, Lord of Verminion, game machines), play with the inn's Toy Chest, dance (every dance emote counts, and you can add your own, such as a modded one), or spend time at a venue you've registered. |
| **Social** | Are in a party, chat, or use emotes while targeting another player. |

**Hardcore mode** adds:

| Need | Fills up when you... |
|---|---|
| **Bladder** | Hold the emote or `/cpose` idle you picked for it. |
| **Hygiene** | Hold the emote or `/cpose` idle you picked for it. Drains faster in combat. |

These needs fill up while you hold the emote or pose: 10% every second by default (adjustable), so a full
refill takes about ten seconds. Pick an emote that **loops** (keeps playing until you move); a one-off
emote only counts for the moment it plays. `/cpose` idles count while you stand or sit still in them.

They're meant to be paired with Penumbra animation mods that replace those emotes or poses. To pick one, start the
looping emote, or get into the pose (stand or `/groundsit`, then `/cpose` until it's the right one), and click
*Use what I'm doing now*. You can also pick an emote from the list.

Needs only drain while you're logged in. How fast each one drains (and how fast things refill it) can be
changed in the plugin.

## Moodles

When a need drops low, it gets a Moodle. As it drops further, the Moodle gains stacks (1 to 3) and its
name changes, for example *Peckish*, then *Hungry*, then *Starving*. When every need is met you get a
*Serene* buff instead. Icons, names and descriptions can all be changed.

**Moodles setting needed:** in Moodles' settings, turn on *Allow other plugins apply Moodles* and, under
it, *Allow applying moodles from party members* (or *from everyone*). Creature Comforts applies the Moodles as you, and you count as a member
of your own party. The plugin tells you if Moodles is turning it away.

Works nicely with [Outer Voice](https://github.com/Historian-90/OuterVoice): make a custom event with a
Moodle condition such as "Starving" and your character will say something about it.

## Installing

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, add
   `https://raw.githubusercontent.com/Historian-90/CreatureComforts/main/repo.json`, tick it and save.
3. Open the plugin installer (`/xlplugins`), search for **Creature Comforts** and install it.
4. Install **Moodles** too, if you haven't already.

Open the window with `/comforts` (or `/needs`).

There's also a **mini display**: a small set of need icons and bars you can keep on your HUD. Drag it where you
want it, right-click it for options, and lock it so clicks pass through to the game. `/comforts hud` shows or
hides it, `/comforts lock` locks or unlocks it.

Choose how needs show with **Show as** at the top of the window: **Moodles**, **Mini display**, or **Both**.
With the mini display only, Creature Comforts doesn't use Moodles at all, so Moodles isn't required.

## For plugin developers

Other plugins can read and nudge needs over Dalamud IPC:

| Name | Type |
|---|---|
| `CreatureComforts.ApiVersion` | `Func<int>` |
| `CreatureComforts.GetNeeds` | `Func<Dictionary<string, float>>`: active needs, 0-100 (100 = satisfied) |
| `CreatureComforts.GetLevels` | `Func<Dictionary<string, int>>`: 0 (fine) to 3 (empty) |
| `CreatureComforts.AddToNeed` | `Action<string, float>`: add to a need (negative drains) |
| `CreatureComforts.LevelChanged` | event `Action<string, int>`: a need's level changed |

Need names: `Hunger`, `Sleep`, `Fun`, `Social`, `Bladder`, `Hygiene`.

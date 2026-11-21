# Changelog

## 0.1.7
- Fixed the "Emotes that count as dancing" list gaining another copy of the default dances every time the plugin loaded. Repeats are cleaned up automatically.
- Every dance in the game now counts for Fun, including Manderville Mambo, Lali Hop, Lop Hop, Breaking, Señor Sabotender, Heel Toe and Goobbue Do. Any emote with "dance" in its name or command counts automatically.
- Add your own dances (for example an emote you've replaced with a dance mod): pick from the emote list, click "Add what I'm doing now" while emoting, or type a name. Names that don't match an emote are flagged.
- Removed the Paint It emotes and Most Gentlemanly from the default dance list (they aren't dances).

## 0.1.6
- Fixed a refill pose saved while moving or mid-emote ("Pose type 255") filling the need whenever you moved instead of while you held still. Such poses are cleared; set yours again.
- "Use my current pose" is now "Use what I'm doing now": click it during a looping emote to make that the refill emote, or in a /cpose idle to make that the refill pose.

## 0.1.5
- Moodles drops every Moodle on your character when you change zones once a plugin has applied one. Creature Comforts now puts its Moodles back right away, during the loading screen when it can, so they return without the pop-up.
- New option (on by default): also put back your other Moodles after a zone change. Timed ones restart their timer.
- No more false "Moodles is blocking us" warning after a zone change.

## 0.1.4
- Moodles stay put through zone changes, loading screens and duty starts instead of popping up again.
- Loading the plugin or logging in updates your existing need Moodles in place rather than re-adding them.
- The Moodles page shows the last change Creature Comforts made and why.

## 0.1.3
- Fixed standing /cpose idles not filling Bladder or Hygiene (sitting ones already worked).
- Change Pose is no longer offered as a refill emote; use "Use my current pose" for poses instead.

## 0.1.2
- Bladder and Hygiene now fill while you hold their emote or /cpose idle (10% a second by default, adjustable) instead of all at once. Pick an emote that loops.
- New default icons for Social, Hygiene and the Serene buff.

## 0.1.1
- Bladder and Hygiene can also be refilled by a /cpose idle (standing, sitting on the ground and so on): get into the pose and click "Use my current pose".
- Long text and tooltips now wrap instead of running off the window.
- The "Moodles is blocking us" message now names the exact Moodles checkbox to tick.

## 0.1.0
- First version: Hunger, Sleep, Fun and Social needs, plus Bladder and Hygiene in hardcore mode.
- Needs show as Moodles that stack up as a need gets worse, and a Serene buff when everything is met.
- Logging out in an inn room or your own home counts as sleep.
- Venues you register fill your Fun need while you're there.

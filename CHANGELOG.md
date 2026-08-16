# Changelog

## Unreleased

Editing the config now actually changes a trinket you are wearing. Until this, it did not,
whatever the three previous releases claimed.

The mod wrote its numbers to the item prefabs and nothing else, on the understanding that an
item in your inventory shares its prefab's data. `ItemData.Clone` is a `MemberwiseClone`, which
copies the reference rather than the contents, so on paper writing one writes both. Measured in
a running game, it does not: with a trinket equipped, the prefab was set to 42 while the item
being worn stayed at 99, and the player's max adrenaline stayed with the item.

What hid it is that a fresh load looks perfect. The inventory is rebuilt from the prefabs after
the retune runs, so everything picks up the new number on the way in. That is why restarting
appeared to be the fix, and why the mod's own log agreed with itself while a player watching
his own trinket kept saying nothing had changed. He was right.

Carried items are now written along with the prefabs. The log reports both counts, as
`retuned N, plus M in the player's inventory`.

## 1.0.2 - 2026-08-16

Config changes no longer depend on the mod being told about them.

A player reported that edits still only took effect after reloading the world, on a build where
reading the file, noticing the edit and applying it had each been checked here. His screenshots
ruled out the remaining explanation: the game rebuilds an item's tooltip every frame while the
inventory is open, so a stale tooltip was never the answer and the value genuinely was not
changing for him.

Rather than guess at his machine a third time, the notification is now an optimisation instead
of the mechanism. Every trinket is recomputed from its original once a second and only what
differs is written, which costs a lookup and a float compare per item. Whatever fails to
arrive, the values are right within a second.

It also says so. If that sweep ever finds work to do, it logs `Swept up N trinket(s) the config
change did not reach`, which turns a silent failure into a line in the log.

Also adds, under `Verbose`, a line reporting the player's live max adrenaline whenever it
changes. That is the number the bar is drawn from and the threshold the trinket fires at, so it
can be read rather than inferred.

Fixes editing the config needing a restart, which is what 1.0.0 said it did not need.

Three separate causes, found after a player reported it. Any one of them is enough to make the
feature look broken.

- The file watcher is no longer relied on by itself. It runs on Unity's Mono rather than
  desktop .NET, and a mod manager will often put a profile behind a junction or a symlink,
  which a watcher does not see through. The file's write time is now checked once a second
  instead, which has none of those failure modes. The watcher is kept only to make it
  immediate when it does work.
- The settle delay ran on scaled time, and a singleplayer game is paused while you are
  alt-tabbed out editing the config. That is exactly when the timer needed to run, so the
  reload arrived only once you came back and unpaused. It runs on unscaled time now.
- Turning `Verbose` on did not itself trigger anything, so it printed nothing until some other
  change happened to cause a pass. Anyone switching logging on to check whether live editing
  worked would have found silence and concluded it did not.

## 1.0.0 - 2026-08-16

First release.

Surge changes how much adrenaline a trinket needs before it fires. Every default is a no-op,
so installing it and changing nothing leaves the game exactly as it was.

- Set an exact value per trinket, scale them all with a multiplier, or give them one flat
  number.
- Editing the config applies straight away, with the game running and without leaving the
  world.
- No dependencies.

Played and working. Confirmed on a running game: all thirteen trinkets are found and retuned,
per-trinket values apply from a cold start, editing the config retunes without a restart,
values are always recomputed from the originals so nothing compounds across reloads, and the
mod loads and works with no other plugin installed.

Worth knowing before turning the multiplier up a long way. Fill and decay are both curves over
how full the bar is, so they stretch with it, but the grace period before an idle bar starts
decaying is a fixed number of seconds and does not. A much longer bar gives a lull in a fight
more chance to eat into it, and against things that die too fast to keep it fed the payoff can
get out of reach.

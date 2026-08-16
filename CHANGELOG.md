# Changelog

## 1.0.1 - 2026-08-16

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

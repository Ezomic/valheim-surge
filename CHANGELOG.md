# Changelog

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

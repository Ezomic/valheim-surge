# Changelog

## 1.0.0 — 2026-08-16

First public release.

Surge configures how much adrenaline a trinket needs before it fires its effect. Every default
is a no-op: install it and change nothing, and the game is exactly as it was.

- Per-trinket absolute values, a global multiplier, or one flat value for everything.
- Editing the config file applies immediately, with the game running and without leaving the
  world. The useful setting here is a feel judgement rather than a fact, and you find it by
  fighting something, changing the number, and fighting it again.
- No dependencies. Longhouse Core is used for its version gate when it happens to be
  installed, and the mod runs perfectly well without it.

### Tested

Played in game and confirmed working, including with a trinket equipped and the bar charging
at the changed rate.

Also confirmed against a running game: all 13 trinkets are found and retuned, per-trinket
values apply from a cold start, a config edit retunes live, values are always recomputed from
the originals so a multiplier never compounds across reloads or restarts, and the mod loads
and works with no other plugin present at all.

One thing worth knowing before turning the multiplier up a long way: gain and decay are both
curves over how full the bar is, so they stretch with it, but the grace period before an idle
bar starts decaying is a fixed number of seconds and does not. A much longer bar therefore
gives a lull in a fight more chance to eat into it, and the payoff can become hard to reach
against enemies that die too quickly to keep it fed.

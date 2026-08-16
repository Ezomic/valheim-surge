# Surge

Configure the max adrenaline granted by trinkets.

Built against the installed game (Unity 6000.0.61, BepInEx 5.4.23.3, Harmony 2.9).

## Installing

Needs BepInEx, and nothing else. This is one DLL with no dependencies — through a mod manager
it is a single install, and by hand it is:

1. Put `Surge.dll` in `BepInEx/plugins/Surge/`.
2. Start the game once and quit. That first run writes the config file; it does not exist
   until the mod has loaded at least one time, which is the usual reason people think the mod
   is broken when nothing has gone wrong.

Out of the box **nothing changes** — every default is deliberately a no-op, so installing it
and not editing anything leaves the game exactly as it was.

## Changing the settings

The config file is:

```
BepInEx/config/ezomic.valheim.surge.cfg
```

Open it in any text editor. Every setting carries a comment above it explaining what it does,
so the file is the documentation. The two you are likely to want:

**Make every trinket take longer to fire.** Its effect goes off when the bar fills, so a bigger
number means a longer charge:

```ini
Multiplier = 2
```

**Set exact values on specific trinkets, leaving the rest alone.** Absolute numbers, not
multipliers, and it needs `Multiplier = 1` or the multiplier still applies to everything you
did not name:

```ini
Multiplier = 1
PerTrinket = TrinketBronzeHealth=35,TrinketFlametalStaminaHealth=45
```

The prefab names are in the table further down, and setting `Verbose = true` makes the mod list
all of them in `BepInEx/LogOutput.log` along with what it changed each one to.

**Save the file and the change applies immediately** — no restart, and no need to leave the
world. See [Editing the config while the game runs](#editing-the-config-while-the-game-runs).

## What adrenaline actually is

A trinket gives you an adrenaline bar. Parrying, dodging and staggering fill it; missing an
attack and taking unblocked damage drain it, and it decays on its own after a short delay.
When it reaches the top, the trinket's own status effect fires and the bar resets to empty.

So the max is not a stat you want more of. It is a **charge time**, and it is the amount the
bar must reach before the trinket fires. A higher max means a longer run of good fighting
before the effect pays out; a lower one means it pays out more often for less. That is the
knob this mod exposes.

It scales close to linearly, because both the fill rate and the decay rate are curves over
*how full* the bar is rather than fixed amounts — so they stretch along with the bar, and 2
really does mean about twice as long.

One thing does not stretch with it, and it matters when raising the value: **the grace period
before an idle bar starts decaying** is a fixed number of seconds. A longer bar therefore
gives a lull in the fight more chance to eat into it, so high values are disproportionately
harder than the number suggests, and the payoff can become hard to reach against enemies that
die too quickly to keep the bar fed.

Nothing else competes with the setting. The game does contain a second adrenaline payoff — a
list of tiered status effects on `Player` keyed to *fixed* amounts rather than to fractions of
the max, which would therefore not move when this setting changes. It is worth knowing about
and worth dismissing: read off a live player, **that list is empty**. The trinket's own effect
is the only payoff there is, so the max really is the whole of the lever. `Verbose` prints the
tiers, so if a future game version fills them in it will say so rather than going quietly
wrong.

Vanilla sets a different max per trinket tier, which is a real balance decision. That is why
the default here is a multiplier rather than a flat number: it keeps the designed spread
between an iron trinket and a flametal one, and it gives modded trinkets a sensible value
for free.

## The vanilla numbers

Read off the running game rather than a wiki, by turning `Verbose` on for one launch:

| Trinket | Max | | Trinket | Max |
| --- | --- | --- | --- | --- |
| `TrinketChitinSwim` | 10 | | `TrinketCarapaceEitr` | 65 |
| `TrinketBronzeHealth` | 50 | | `TrinketFlametalEitr` | 70 |
| `TrinketBronzeStamina` | 50 | | `TrinketScaleStaminaDamage` | 75 |
| `TrinketSilverDamage` | 55 | | `TrinketSilverResist` | 80 |
| `TrinketBlackStamina` | 60 | | `TrinketBlackDamageHealth` | 85 |
| `TrinketIronStamina` | 60 | | `TrinketFlametalStaminaHealth` | 100 |
| `TrinketIronHealth` | 65 | | | |

Thirteen trinkets, and the spread runs 10 to 100 — which is the argument for the multiplier
default in one table. These are not tiers of one number with a couple of outliers; the value
is doing per-trinket balance work. `TrinketChitinSwim` at 10 charges its effect almost
constantly, `TrinketFlametalStaminaHealth` at 100 is a long earn. A flat value throws all of
that away, so `FlatValue` exists but is off.

Note also that `TrinketFlametaStaminaHealth` — the misspelled name that appears in the game's
own asset manifest — is not among them. The manifest lists what is on disk, not what loads.

## Settings

| Setting | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Off leaves every trinket at its vanilla value |
| `Multiplier` | `1` | Scales each trinket's vanilla max. `0.5` = effect fires twice as often |
| `FlatValue` | `0` | Same exact max for every trinket. `0` = off |
| `PerTrinket` | *(empty)* | `TrinketIronHealth=80,TrinketSilverDamage=120` — beats both of the above |
| `Minimum` | `1` | Floor, so a heavy multiplier cannot round a trinket down to no bar at all |
| `PlayerBase` | `-1` | Max adrenaline with no trinket on. `-1` leaves the game alone |
| `Verbose` | `false` | Logs every trinket found and what it became — turn on once to get the prefab names |

The defaults are deliberately a no-op. Installing this mod and not editing the config
changes nothing at all; the log line telling you what it found is the only difference.

`PlayerBase` is the odd one out and is left alone by default. Every trinket's value is
*added* to a base the player carries, and that base looks like it is 0 in vanilla — the bar
only shows up once something is equipped. Raising it gives you an adrenaline bar permanently.
The startup log prints the real vanilla number so you can check rather than take that on
trust.

## How it works

The number a trinket grants is a plain float on `ItemDrop.ItemData.SharedData`, and
`Player.UpdateModifiers` reads it back off `m_shared` by reflection every frame, summing one
field across all eight equipment slots. So this mod writes to the trinket prefabs in
`ObjectDB` and patches nothing else. The bar resizes, the tooltip updates, and the
full-adrenaline effect fires at the new threshold, all through the game's own code.

Two things fall out of that. Because `UpdateModifiers` runs per frame rather than on equip, a
config change lands live — no re-equipping and no reload. And because the original value is
captured the first time an item is seen and every result computed from it, the multiplier
never compounds across the several times `ObjectDB` is built in a session.

## Editing the config while the game runs

Save the `.cfg` and the change takes effect. There is no keybind and nothing to press.

This needs saying because it is not how BepInEx normally behaves: BepInEx 5 does not watch
config files, so without help the only way to try a different multiplier is to edit and
restart, which is a full world load per value. Surge watches its own file, waits for the
writes to settle, reloads, and retunes. Since the game re-reads the result every frame, the
new number is live before you have alt-tabbed back.

That is worth more here than in most mods, because the useful setting is a feel judgement
rather than a fact. You find it by fighting something with one value, changing it, and
fighting the same thing again.

## Multiplayer

Mechanically this is client-side: adrenaline is worked out entirely on the owning client and
the max never travels, so a player running this mod gets their own numbers whether or not
anyone else does. That is what makes it safe to hand to one person in a group.

When Longhouse Core is installed, Surge registers with its version gate so a client and server
that disagree are told about it rather than quietly playing different games. That is a
fairness call and not a safety one — nothing corrupts if only one side runs it, since no
prefab is registered and no ZDO is written, so there is no saved data at risk. It is precisely
because nothing corrupts that the dependency can be optional at all.

Without Core it simply runs, and says so in the log.

`ObjectDB.CopyOtherDB` is patched as well as `Awake`, because a client rebuilds its item
database from the server's copy on join — patching only `Awake` would let a vanilla server
silently undo the mod the moment you connect. Running it on a dedicated server as well puts
everyone on the same numbers without each player having to match cfg files.

## Status

Tested in game and working, confirmed 2026-08-16 with a trinket equipped and the bar charging
at the changed rate.

Also confirmed against a running client:

- All 13 trinkets resolve out of `ObjectDB` and their vanilla values read back correctly.
- The defaults really are a no-op: `Multiplier = 1` reported `retuned 0`.
- Editing the `.cfg` with the game running retuned all 13 without a restart.
- **The player's base max adrenaline is 0**, read off a live player rather than assumed. So a
  trinket's own number is the whole of the max, and the settings here are the whole of the
  lever. It also means `PlayerBase` is a real option rather than a rounding tweak: raising it
  above 0 is the only way to have an adrenaline bar with no trinket equipped.
- **The player's tiered adrenaline effects list is empty**, also read rather than assumed. The
  trinket's own effect is the only thing the bar pays out, so nothing competes with the max
  for control of when that happens.
- `Multiplier = 2` doubled all 13 from their **vanilla** values, in a session where the
  previous run had left them at 0.25. Bronze went 50 to 100, not 25 — the originals are what
  gets scaled, across restarts as well as within a session.
- `PerTrinket` names two items and touches exactly those two, from a cold start rather than
  only through a live reload: bronze 50 to 150, iron 65 to 200, the other eleven reported
  `unchanged`, no parse warnings.
- The base of 0 and the empty tier list both reproduced on a second, separate session, so
  neither is an artefact of one run.
- Loading a world runs the tune a second time and it reported `retuned 0` with every trinket
  `unchanged`. That is the anti-compounding guard doing its job — had it recomputed from the
  current values instead of the originals, a `Multiplier` of 0.25 would have squared to
  0.0625 and bronze would have read 3.13 rather than holding at 12.5.

One path remains reasoned about rather than exercised: **`ObjectDB.CopyOtherDB`**, which runs
when joining a server that does not have this mod. Every test session so far hosted its own
world, so the client was the server and that path never ran. It is patched for the same reason
`Awake` is, and the failure it guards against would be the mod silently reverting on connect.

## License

MIT. See [LICENSE](LICENSE).

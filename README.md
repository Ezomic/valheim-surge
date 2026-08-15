# Surge

Configure the max adrenaline granted by trinkets.

Built against the installed game (Unity 6000.0.61, BepInEx 5.4.23.3, Harmony 2.9).

## What adrenaline actually is

A trinket gives you an adrenaline bar. Parrying, dodging and staggering fill it; missing an
attack and taking unblocked damage drain it, and it decays on its own after a short delay.
When it reaches the top, the trinket's own status effect fires and the bar resets to empty.

So the max is not a stat you want more of. It is a **charge time**. A higher max means a
longer run of good fighting before the effect pays out; a lower one means it pays out more
often for less. That is the whole knob this mod exposes, and it is the one worth exposing,
because the gain rates are unchanged — halving the max really does double how often the
effect goes off, with no other side effects.

Vanilla sets a different max per trinket tier, which is a real balance decision. That is why
the default here is a multiplier rather than a flat number: it keeps the designed spread
between an iron trinket and a flametal one, and it gives modded trinkets a sensible value
for free.

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

## Multiplayer

Client-side. Adrenaline is worked out entirely on the owning client and the max never travels,
so a player running this mod gets their own numbers whether or not anyone else does.

`ObjectDB.CopyOtherDB` is patched as well as `Awake`, because a client rebuilds its item
database from the server's copy on join — patching only `Awake` would let a vanilla server
silently undo the mod the moment you connect. Running it on a dedicated server as well puts
everyone on the same numbers without each player having to match cfg files.

## Status

Builds. **Not yet tested in game.**

## License

MIT. See [LICENSE](LICENSE).

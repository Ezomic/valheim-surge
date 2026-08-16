# Surge

Change how much adrenaline a trinket needs before it goes off.

## Installing

Needs BepInEx. Nothing else. Through a mod manager it is one install. By hand, put
`Surge.dll` in `BepInEx/plugins/Surge/`.

Then start the game once and quit. That first run writes the config file. It does not exist
before the mod has loaded, which is the usual reason people think it is broken.

Nothing changes until you edit that file. Every default is a no-op on purpose.

## Changing the settings

The file is `BepInEx/config/ezomic.valheim.surge.cfg`. Open it in any text editor. Every
setting has a comment above it, so the file explains itself.

To make every trinket take longer to fire:

```ini
Multiplier = 2
```

To set exact numbers on some trinkets and leave the rest alone. These are absolute values, not
multipliers, and `Multiplier` has to be 1 or it still applies to everything you did not name:

```ini
Multiplier = 1
PerTrinket = TrinketBronzeHealth=35,TrinketFlametalStaminaHealth=45
```

Save the file and it applies straight away. No restart, and you do not have to leave the
world. That is deliberate. The right number here is a feel judgement rather than a fact, and
you find it by fighting something, changing the value, and fighting the same thing again.

One thing that will make you think it did not work. Valheim writes an item's tooltip once, at
the moment your cursor arrives on it, and never rewrites it while it sits there. So if you are
hovering a trinket when the change lands, the tooltip keeps showing the old number. Move the
cursor off it and back on and you will see the new one. The crafting panel is the same, it
refreshes when you click a different recipe. Nothing is wrong when this happens and the value
in the game has already changed, it is just the interface not being asked again.

Set `Verbose = true` and the mod lists every trinket it found in `BepInEx/LogOutput.log`, with
what it changed each one to.

## What the number is

A trinket gives you an adrenaline bar. Parrying, dodging and staggering fill it. Missing an
attack and taking unblocked damage drain it, and it decays on its own after a short delay.
When it reaches the top, the trinket's effect fires and the bar empties.

So the max is not a stat you want more of. It is a charge time. Raise it and the effect takes
a longer run of good fighting to earn. Lower it and it pays out more often for less.

It scales close to linearly. Fill and decay are both curves over how full the bar is rather
than fixed amounts, so they stretch along with it, and 2 really is about twice as long.

One thing does not stretch. The grace period before an idle bar starts decaying is a fixed
number of seconds, so a longer bar gives a lull in the fight more chance to eat into it. High
values are harder than the number makes them look, and against things that die too fast to
keep the bar fed the payoff can get out of reach entirely.

## The trinkets

Read off the game rather than a wiki. The prefab name is what `PerTrinket` wants.

| Trinket | Prefab | Vanilla max |
| --- | --- | --- |
| Fins of Destiny | `TrinketChitinSwim` | 10 |
| Heart of the Forest | `TrinketBronzeHealth` | 50 |
| Bronze Pendant | `TrinketBronzeStamina` | 50 |
| Wolf Sight | `TrinketSilverDamage` | 55 |
| Evasion Mantle | `TrinketBlackStamina` | 60 |
| Nimble Anklet | `TrinketIronStamina` | 60 |
| Iron Brooch | `TrinketIronHealth` | 65 |
| Pulsating Earrings | `TrinketCarapaceEitr` | 65 |
| Jörmundling | `TrinketFlametalEitr` | 70 |
| Resounding Shackle | `TrinketScaleStaminaDamage` | 75 |
| Crystal Heart | `TrinketSilverResist` | 80 |
| Bracelets of the Brave | `TrinketBlackDamageHealth` | 85 |
| Brimstone | `TrinketFlametalStaminaHealth` | 100 |

Thirteen of them, running 10 to 100. That spread is why the default is a multiplier rather
than one flat number. The value is doing per-trinket balance work, not marking tiers. Fins of
Destiny at 10 charges almost constantly and Brimstone at 100 is a long earn, and a single flat
value throws all of that away. `FlatValue` is there if you want to flatten it anyway.

## Settings

| Setting | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Off leaves every trinket at its vanilla value |
| `Multiplier` | `1` | Scales each trinket's vanilla max. `0.5` fires twice as often |
| `FlatValue` | `0` | One max for every trinket. `0` is off |
| `PerTrinket` | *(empty)* | `TrinketIronHealth=80,TrinketSilverDamage=120`. Beats both of the above |
| `Minimum` | `1` | Floor, so a heavy multiplier cannot round a trinket down to no bar at all |
| `PlayerBase` | `-1` | Max adrenaline with no trinket on. `-1` leaves the game alone |
| `Verbose` | `false` | Lists every trinket and what it became |

`PlayerBase` is the odd one and is left alone by default. A trinket's value is added to a base
the player carries, and in vanilla that base is 0, which is why the bar only appears once you
equip something. Raise it and you have an adrenaline bar permanently.

## How it works

The number a trinket grants is a plain float on `ItemDrop.ItemData.SharedData`, and
`Player.UpdateModifiers` reads it back off `m_shared` by reflection every frame, summing that
one field across all eight equipment slots. So the mod writes to the trinket prefabs in
`ObjectDB` and patches nothing else. The bar resizes, the tooltip updates and the effect fires
at the new threshold, all through the game's own code.

Two things follow from that. The config change lands live, because the game re-reads the field
every frame rather than on equip. And the multiplier never compounds, because each trinket's
original value is captured the first time it is seen and every result is computed from that
rather than from whatever is currently set.

BepInEx does not watch config files, so the mod watches its own, waits for the writes to
settle, and retunes.

## Multiplayer

Client-side. Adrenaline is worked out entirely on the owning client and the max never travels,
so you get your own numbers whether or not anyone else runs this.

`ObjectDB.CopyOtherDB` is patched as well as `Awake`, because a client rebuilds its item
database from the server's copy when it joins. Patching only `Awake` would let a vanilla
server quietly undo the mod the moment you connect.

## Status

Played and working, 2026-08-16.

Confirmed on a running game: all thirteen trinkets are found and retuned, per-trinket values
apply from a cold start, editing the config retunes without a restart, values are always
recomputed from the originals so nothing compounds across reloads, and the mod loads and works
with no other plugin installed.

One path has been reasoned about but never run: `ObjectDB.CopyOtherDB`, which fires when you
join a server that does not have the mod. Every test so far hosted its own world, so the
client was the server and that path never came up.

## License

MIT. See [LICENSE](LICENSE).

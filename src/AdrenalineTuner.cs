using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Surge
{
    /// <summary>
    /// Rewrites m_maxAdrenaline on the trinket prefabs in ObjectDB.
    ///
    /// This is the whole mod, and it is deliberately the smallest possible change. The
    /// number a trinket grants is a plain float on ItemDrop.ItemData.SharedData, and
    /// Player.UpdateModifiers reads it back off m_shared by reflection every frame, summing
    /// one field across all eight equipment slots. So editing the prefab's shared data is
    /// not a shortcut around the game's system - it *is* the input to the game's system.
    /// Nothing needs patching: the bar resizes, the tooltip updates, and the full-adrenaline
    /// effect fires at the new threshold, all through vanilla code.
    ///
    /// Two consequences worth knowing:
    ///
    /// Because UpdateModifiers runs per frame rather than on equip, a config change lands
    /// live. No re-equipping, no reload.
    ///
    /// Because m_shared is shared by every instance of an item rather than copied per item,
    /// one write covers the one in your pack, the one in a chest and the one on the ground.
    /// That is also why the original value has to be captured the first time an item is
    /// seen and never overwritten: ObjectDB is built more than once per session, so a mod
    /// that multiplies whatever it currently finds squares its own multiplier on the second
    /// pass.
    /// </summary>
    internal static class AdrenalineTuner
    {
        private static readonly Dictionary<string, float> Originals = new Dictionary<string, float>();

        /// <summary>
        /// Writes the same targets into the items the player is actually carrying.
        ///
        /// This is the whole reason a config change used to need a world reload, and it took
        /// measuring to find rather than reading. ItemData.Clone is a MemberwiseClone, which
        /// copies the *reference* to m_shared, so on paper an inventory item and its prefab
        /// are the same object and writing one writes both. In a running game they do not
        /// behave that way: with a trinket equipped, the prefab was set to 42 while the item
        /// being worn stayed at 99 and the player's max adrenaline stayed at 99 with it.
        ///
        /// What made it convincing as a prefab-only job is that a fresh load looks perfect.
        /// The inventory is rebuilt from the prefabs after the retune has run, so every item
        /// picks up the new number on the way in. That is exactly why restarting appeared to
        /// be the fix, and why every check made against this mod's own log agreed with itself
        /// while a player watching his own trinket kept saying it had not changed. He was
        /// right.
        ///
        /// Keyed on m_dropPrefab.name so it lands on the same entry the prefab pass used, and
        /// harmless in the case where the reference genuinely is shared: the value is already
        /// correct and nothing is written.
        /// </summary>
        private static int ApplyToInventory()
        {
            var player = Player.m_localPlayer;
            var inventory = player != null ? player.GetInventory() : null;
            if (inventory == null) return 0;

            var changed = 0;

            foreach (var item in inventory.GetAllItems())
            {
                if (item == null || item.m_shared == null || item.m_dropPrefab == null) continue;

                float original;
                if (!Originals.TryGetValue(item.m_dropPrefab.name, out original)) continue;

                var target = Target(item.m_dropPrefab.name, original);
                if (Mathf.Approximately(item.m_shared.m_maxAdrenaline, target)) continue;

                item.m_shared.m_maxAdrenaline = target;
                changed++;
            }

            return changed;
        }

        /// <param name="announce">
        /// False for the periodic sweep, which runs constantly and must say nothing unless it
        /// actually changed something. True for the passes worth reporting: startup, a world
        /// load, and a config change that arrived through the normal event.
        /// </param>
        public static void Apply(bool announce = true)
        {
            var db = ObjectDB.instance;

            // The first ObjectDB.Awake of a session fires against a stub with no items in
            // it. Not a failure and not worth a warning - the real one comes along later
            // and this runs again.
            if (db == null || db.m_items == null || db.m_items.Count == 0) return;

            var changed = 0;
            var found = 0;

            foreach (var prefab in db.m_items)
            {
                if (prefab == null) continue;

                var drop = prefab.GetComponent<ItemDrop>();
                var shared = drop != null && drop.m_itemData != null ? drop.m_itemData.m_shared : null;
                if (shared == null) continue;

                if (!Originals.TryGetValue(prefab.name, out var original))
                {
                    original = shared.m_maxAdrenaline;
                    Originals[prefab.name] = original;
                }

                // Trinkets are the whole of it in vanilla, but the field is on SharedData
                // rather than on trinkets specifically and UpdateModifiers sums it across
                // every slot - so anything that already grants adrenaline is fair game
                // whatever slot it sits in. A trinket granting 0 is still counted, because
                // that is a deliberate thing to configure upward.
                if (shared.m_itemType != ItemDrop.ItemData.ItemType.Trinket && original == 0f) continue;

                found++;

                var target = Target(prefab.name, original);
                if (Mathf.Approximately(shared.m_maxAdrenaline, target))
                {
                    if (!announce) continue;

                    // Two very different states reach this branch, and printing the original
                    // for both made them identical in the log. On the second pass of a
                    // session every configured trinket lands here, so a run with settings
                    // applied read as thirteen lines of vanilla numbers marked "unchanged" -
                    // which looks exactly like the mod having reverted them. Say which it is.
                    if (SurgeConfig.Verbose.Value)
                        SurgePlugin.Log.LogInfo("  " + prefab.name + ": "
                            + (Mathf.Approximately(target, original)
                                ? Show(original) + " (vanilla)"
                                : Show(original) + " -> " + Show(target) + " (already set)"));

                    continue;
                }

                shared.m_maxAdrenaline = target;
                changed++;

                // Always logged when something actually moved, even on a quiet sweep. A sweep
                // that finds work to do means the config change never arrived through the
                // event, which is worth seeing in a log rather than being silently repaired.
                if (SurgeConfig.Verbose.Value || !announce)
                    SurgePlugin.Log.LogInfo("  " + prefab.name + ": " + Show(original) + " -> " + Show(target));
            }

            var live = ApplyToInventory();

            if (announce)
                SurgePlugin.Log.LogInfo("Found " + found + " adrenaline item(s); retuned " + changed
                                        + ", plus " + live + " in the player's inventory.");
            else if (changed > 0 || live > 0)
                SurgePlugin.Log.LogInfo("Swept up " + changed + " prefab(s) and " + live
                                        + " carried item(s) the config change did not reach.");
        }

        /// <summary>
        /// What one item's max adrenaline should be, always computed from its vanilla value
        /// rather than its current one.
        ///
        /// Order is most specific wins: a per-item entry beats the flat value, which beats
        /// the multiplier. An item whose vanilla value is 0 is left at 0 by the multiplier -
        /// scaling nothing gives nothing, and quietly handing adrenaline to a trinket the
        /// game never gave any to would be a surprise rather than a setting.
        /// </summary>
        private static float Target(string prefabName, float original)
        {
            if (!SurgeConfig.Enabled.Value) return original;

            if (SurgeConfig.TryGetOverride(prefabName, out var perItem))
                return Mathf.Max(0f, perItem);

            if (SurgeConfig.FlatValue.Value > 0f)
                return Floor(SurgeConfig.FlatValue.Value);

            if (original == 0f) return 0f;

            return Floor(original * SurgeConfig.Multiplier.Value);
        }

        /// <summary>
        /// Keeps a heavy-handed multiplier from rounding a trinket down to no adrenaline at
        /// all, which switches the bar off rather than merely weakening it. A per-item entry
        /// skips this on purpose - typing an exact number is asking for that exact number.
        /// </summary>
        private static float Floor(float value)
        {
            return Mathf.Max(Mathf.Max(0f, SurgeConfig.Minimum.Value), value);
        }

        /// <summary>
        /// InvariantCulture, not the machine's. This is not cosmetic: the config tells you to
        /// read prefab names out of this log, PerTrinket splits its entries on commas, and
        /// a locale whose decimal separator is a comma prints "85 -> 21,25" by default
        /// and anything copied from it parsed as an entry of 21 followed by junk.
        /// </summary>
        private static string Show(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}

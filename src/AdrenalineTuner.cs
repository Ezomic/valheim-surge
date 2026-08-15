using System.Collections.Generic;
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

        public static void Apply()
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
                    if (SurgeConfig.Verbose.Value)
                        SurgePlugin.Log.LogInfo("  " + prefab.name + ": " + Show(original) + " (unchanged)");

                    continue;
                }

                shared.m_maxAdrenaline = target;
                changed++;

                if (SurgeConfig.Verbose.Value)
                    SurgePlugin.Log.LogInfo("  " + prefab.name + ": " + Show(original) + " -> " + Show(target));
            }

            SurgePlugin.Log.LogInfo("Found " + found + " adrenaline item(s); retuned " + changed + ".");
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

        private static string Show(float value)
        {
            return value.ToString("0.##");
        }
    }
}

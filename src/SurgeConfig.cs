using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;

namespace Surge
{
    internal static class SurgeConfig
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> Multiplier;
        public static ConfigEntry<float> FlatValue;
        public static ConfigEntry<string> PerTrinket;
        public static ConfigEntry<float> Minimum;
        public static ConfigEntry<float> PlayerBase;
        public static ConfigEntry<bool> Verbose;

        /// <summary>
        /// Parsed form of <see cref="PerTrinket"/>. Rebuilt whenever that entry changes
        /// rather than reparsed per item, because the tuner walks every item in ObjectDB.
        /// </summary>
        private static Dictionary<string, float> _overrides;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Surge", "Enabled", true,
                "Retune the max adrenaline that trinkets grant. Off leaves every trinket at "
                + "its vanilla value; nothing else in the adrenaline system is touched either "
                + "way.");

            // A multiplier rather than a flat number, so a mod that adds its own trinkets
            // lands somewhere sensible instead of being flattened to one value alongside
            // vanilla's, and so the designed spread between an iron trinket and a flametal
            // one survives the change.
            Multiplier = config.Bind("Surge", "Multiplier", 1f,
                "Scales every trinket's vanilla max adrenaline. 1 = unchanged. Below 1 the "
                + "bar fills sooner, so the trinket's full-adrenaline effect fires more often; "
                + "above 1 it takes longer and fires less. This is the whole of the balance "
                + "lever - gain rate is untouched, so halving this really does double how "
                + "often the effect pops.");

            FlatValue = config.Bind("Surge", "FlatValue", 0f,
                "Give every trinket this exact max adrenaline, ignoring Multiplier. 0 = off. "
                + "Use it to flatten the tiers deliberately; note that it applies to modded "
                + "trinkets too, which is usually not what you want.");

            PerTrinket = config.Bind("Surge", "PerTrinket", "",
                "Per-item values, beating both Multiplier and FlatValue. Comma-separated "
                + "Prefab=Number, e.g. TrinketIronHealth=80,TrinketSilverDamage=120. Prefab "
                + "names are the ones in the log line this mod writes on startup - turn "
                + "Verbose on once and they are all listed.");

            // A multiplier small enough to round a trinket to zero would not merely weaken
            // it: GetMaxAdrenaline of 0 hides the bar and switches the whole system off for
            // that trinket, which reads as the mod having broken rather than as a setting.
            Minimum = config.Bind("Surge", "Minimum", 1f,
                "Floor for any trinket that grants adrenaline at all. A computed value below "
                + "this is raised to it. Set to 0 to allow a trinket to be tuned down to no "
                + "adrenaline at all, which disables the bar while it is equipped.");

            // Separate from the trinket knobs because it is a different quantity: the base
            // every trinket adds to, which vanilla appears to leave at 0 (the bar only shows
            // up with a trinket on). Left alone by default because that appearance is
            // inferred, and the startup log prints the real number so it can be checked.
            PlayerBase = config.Bind("Surge", "PlayerBase", -1f,
                "Max adrenaline the player has with no trinket equipped, which every trinket "
                + "then adds to. -1 leaves the game's own value alone. Setting this above 0 "
                + "gives you an adrenaline bar permanently, trinket or not.");

            Verbose = config.Bind("Diagnostics", "Verbose", false,
                "Log every trinket found and what its max adrenaline was changed to.");

            PerTrinket.SettingChanged += (s, e) => _overrides = null;
        }

        /// <summary>
        /// The configured value for one prefab, or false when it has no entry.
        /// Case-insensitive: the names are typed by hand into a text file.
        /// </summary>
        public static bool TryGetOverride(string prefabName, out float value)
        {
            if (_overrides == null) Parse();

            return _overrides.TryGetValue(prefabName, out value);
        }

        private static void Parse()
        {
            _overrides = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in (PerTrinket.Value ?? "").Split(','))
            {
                var text = entry.Trim();
                if (text.Length == 0) continue;

                var split = text.IndexOf('=');
                if (split <= 0)
                {
                    SurgePlugin.Log.LogWarning("PerTrinket: ignoring '" + text + "' - expected Prefab=Number.");
                    continue;
                }

                var name = text.Substring(0, split).Trim();

                // InvariantCulture on purpose. This machine is on a Dutch locale, where the
                // decimal separator is a comma - which is also the entry separator here, so
                // a value can only ever arrive with a dot in it.
                if (!float.TryParse(text.Substring(split + 1).Trim(),
                                    NumberStyles.Float,
                                    CultureInfo.InvariantCulture,
                                    out var value))
                {
                    SurgePlugin.Log.LogWarning("PerTrinket: '" + name + "' has no readable number - ignored.");
                    continue;
                }

                _overrides[name] = value;
            }
        }
    }
}

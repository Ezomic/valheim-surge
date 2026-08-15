using BepInEx;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;
using UnityEngine;

namespace Surge
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("ezomic.valheim.core", BepInDependency.DependencyFlags.HardDependency)]
    // No BepInProcess. Adrenaline is worked out entirely on the owning client - the max is
    // read off the local player's own equipment every frame and never travels - so this is
    // a client-side mod. It is listed for a dedicated server anyway for the same reason
    // Hoard is: a client rebuilds its item database from the server's copy on join, through
    // ObjectDB.CopyOtherDB. That path is patched below, so a vanilla server no longer undoes
    // the mod, but a server that also runs it keeps everyone on the same numbers without
    // each player having to match cfg files.
    public class SurgePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.surge";
        public const string PluginName = "Surge";
        public const string PluginVersion = "0.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        internal static ManualLogSource Log;

        /// <summary>How long the config file has to sit still before it is read.</summary>
        private const float SettleSeconds = 0.3f;

        private Harmony _harmony;

        /// <summary>The Player the base value was last stamped onto, so respawns get it too.</summary>
        private Player _stamped;

        /// <summary>Work handed over from a config change, to be done on the main thread.</summary>
        private static volatile bool _retune;
        private static volatile bool _restamp;

        /// <summary>
        /// When a noticed file edit should be acted on. Saving a text file commonly lands as
        /// two or three filesystem events, and an editor that writes a temporary file and
        /// renames it over the original briefly leaves nothing readable at the path - so a
        /// reload on the first event reads a half-written file. Waiting out the quiet is
        /// simpler than trying to tell the cases apart.
        /// </summary>
        private float _reloadAt;

        /// <summary>
        /// The game's own base, read off the first player seen rather than assumed. It is
        /// almost certainly 0 - the bar only appears with a trinket equipped - but the C#
        /// field initialiser says 100f and the prefab overrides it, so the honest way to
        /// know is to look.
        /// </summary>
        private static float _vanillaBase = float.NaN;

        private void Awake()
        {
            Log = Logger;
            SurgeConfig.Bind(Config);
            // Everyone, not HostOnly - but for fairness rather than for safety, which is a
            // weaker reason than the one the mods that register prefabs have and is worth
            // being honest about. Nothing here corrupts when only one side runs it: no prefab
            // is registered and no ZDO is written, so there is no hash that can fail to
            // resolve and nothing already standing in a world to lose, and items sync by name
            // and quality rather than by the shared data this touches. What does happen is
            // that two players in the same fight earn their trinket procs at different rates
            // with neither of them being told, and a silent disagreement is the thing Core
            // exists to report.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(ObjectDbPatches));

            // Every knob here is read out of the item database rather than held in memory,
            // so re-running the tuner is all a config change needs. UpdateModifiers reads
            // the result every frame, so it lands without re-equipping anything.
            //
            // Queued rather than applied on the spot, for two reasons. A reload changes
            // several entries and would otherwise walk the item database once per entry,
            // and the handler can arrive off the main thread by way of the file watcher,
            // where touching a Unity object is not allowed.
            SurgeConfig.Enabled.SettingChanged += Queue;
            SurgeConfig.Multiplier.SettingChanged += Queue;
            SurgeConfig.FlatValue.SettingChanged += Queue;
            SurgeConfig.PerTrinket.SettingChanged += Queue;
            SurgeConfig.Minimum.SettingChanged += Queue;

            // The base is stamped onto the Player object rather than read from config each
            // frame, so changing it has to invalidate the stamp. Dropping the remembered
            // reference makes the next Update treat the current player as a new one.
            SurgeConfig.PlayerBase.SettingChanged += (s, e) => _restamp = true;

            ConfigWatcher.Start(Config);

            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        private void OnDestroy()
        {
            ConfigWatcher.Stop();
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        private static void Queue(object sender, System.EventArgs e)
        {
            _retune = true;
        }

        /// <summary>
        /// The one place anything actually happens, because it is the one place guaranteed
        /// to be the main thread.
        /// </summary>
        private void Update()
        {
            if (ConfigWatcher.TakeDirty()) _reloadAt = Time.time + SettleSeconds;

            if (_reloadAt > 0f && Time.time >= _reloadAt)
            {
                _reloadAt = 0f;
                Reload();
            }

            if (_retune)
            {
                _retune = false;
                AdrenalineTuner.Apply();
            }

            StampBase();
        }

        private void Reload()
        {
            try
            {
                // Only entries whose value actually differs raise SettingChanged, so this
                // costs nothing when the file was touched without being meaningfully
                // changed - including when BepInEx itself writes it back out.
                Config.Reload();
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Could not reload the config: " + e.Message);
            }
        }

        /// <summary>
        /// Stamps the configured base onto the local player.
        ///
        /// Not a Harmony patch, because the thing being watched is an object being replaced
        /// rather than a method being called: dying and respawning builds a new Player, and
        /// comparing the reference catches that as well as the first spawn, with one branch
        /// and no hook. It is written every time the reference changes rather than once,
        /// since the new instance comes back with the prefab's value.
        /// </summary>
        private void StampBase()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            if (_restamp) { _restamp = false; _stamped = null; }
            if (player == _stamped) return;

            _stamped = player;

            if (float.IsNaN(_vanillaBase))
            {
                _vanillaBase = player.m_maxAdrenaline;
                Log.LogInfo("Player base max adrenaline (before trinkets) is "
                            + _vanillaBase.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ".");

                if (SurgeConfig.Verbose.Value) LogTiers(player);
            }

            player.m_maxAdrenaline = SurgeConfig.PlayerBase.Value < 0f
                ? _vanillaBase
                : SurgeConfig.PlayerBase.Value;
        }

        /// <summary>
        /// Prints the player's own tiered adrenaline effects, which this mod does not touch.
        ///
        /// Worth logging because they are easy to mistake for the thing being configured, and
        /// because raising a trinket's max changes how they relate. There are two separate
        /// payoffs in the adrenaline system: these tiers, which fire at an *absolute* amount
        /// of adrenaline, and the trinket's own effect, which fires at the max and empties the
        /// bar. Raise the max and the tiers do not move with it - so you reach the same buffs
        /// at much the same point as before and then keep fighting well past them, spending
        /// considerably longer in the top tier before the trinket finally pays out.
        ///
        /// The list is walked with var because the element type is nested and awkward to
        /// name; nothing here needs to name it.
        /// </summary>
        private static void LogTiers(Player player)
        {
            var tiers = player.m_adrenalineEffects;
            if (tiers == null || tiers.Count == 0)
            {
                Log.LogInfo("Player has no tiered adrenaline effects.");
                return;
            }

            foreach (var tier in tiers)
            {
                var name = tier.m_se != null ? tier.m_se.name : "(none)";
                Log.LogInfo("  player adrenaline tier at "
                            + tier.m_rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                            + ": " + name);
            }
        }
    }

    internal static class ObjectDbPatches
    {
        /// <summary>
        /// Both entry points, because both really happen: Awake builds the database at
        /// startup, and CopyOtherDB rebuilds it from the server's copy when a world loads.
        /// Patching only Awake means the server's untouched values quietly replace yours the
        /// moment you join a game.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static void TuneOnAwake()
        {
            AdrenalineTuner.Apply();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static void TuneOnCopy()
        {
            AdrenalineTuner.Apply();
        }
    }
}

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Surge
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
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

        private Harmony _harmony;

        /// <summary>The Player the base value was last stamped onto, so respawns get it too.</summary>
        private Player _stamped;

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

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(ObjectDbPatches));

            // Every knob here is read out of the item database rather than held in memory,
            // so re-running the tuner is all a config change needs. UpdateModifiers reads
            // the result every frame, so it lands without re-equipping anything.
            SurgeConfig.Enabled.SettingChanged += Retune;
            SurgeConfig.Multiplier.SettingChanged += Retune;
            SurgeConfig.FlatValue.SettingChanged += Retune;
            SurgeConfig.PerTrinket.SettingChanged += Retune;
            SurgeConfig.Minimum.SettingChanged += Retune;

            // The base is stamped onto the Player object rather than read from config each
            // frame, so changing it has to invalidate the stamp. Dropping the remembered
            // reference makes the next Update treat the current player as a new one.
            SurgeConfig.PlayerBase.SettingChanged += (s, e) => _stamped = null;

            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        private static void Retune(object sender, System.EventArgs e)
        {
            AdrenalineTuner.Apply();
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
        private void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null || player == _stamped) return;

            _stamped = player;

            if (float.IsNaN(_vanillaBase))
            {
                _vanillaBase = player.m_maxAdrenaline;
                Log.LogInfo("Player base max adrenaline (before trinkets) is " + _vanillaBase.ToString("0.##") + ".");
            }

            player.m_maxAdrenaline = SurgeConfig.PlayerBase.Value < 0f
                ? _vanillaBase
                : SurgeConfig.PlayerBase.Value;
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

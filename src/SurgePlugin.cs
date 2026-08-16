using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;
using UnityEngine;

namespace Surge
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Soft, not hard. Surge is the one mod here meant to be handed to someone who wants only
    // it, and a hard dependency that is absent does not degrade gracefully - the plugin never
    // loads at all. Soft still gets the load-order guarantee when Core is present, which is
    // what registering with the gate needs.
    [BepInDependency(CoreGuid, BepInDependency.DependencyFlags.SoftDependency)]
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
        public const string PluginVersion = "1.0.3";
        public const string PluginAuthor = "Robbin Thijssen";

        /// <summary>Core's plugin GUID. Optional - see TryRegisterWithCore.</summary>
        private const string CoreGuid = "ezomic.valheim.core";

        internal static ManualLogSource Log;

        /// <summary>How long the config file has to sit still before it is read.</summary>
        private const float SettleSeconds = 0.3f;

        /// <summary>
        /// How often the config file's write time is checked. One stat a second is nothing,
        /// and it is what makes live editing work on machines where the file watcher does not.
        /// </summary>
        private const float PollSeconds = 1.0f;

        private float _nextPoll;

        /// <summary>Last effective max reported, so the line is written on change only.</summary>
        private float _lastReportedMax = float.NaN;

        /// <summary>Same, for the equipped trinket's own shared data.</summary>
        private float _lastEquippedShared = float.NaN;

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
            TryRegisterWithCore();

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

            // Verbose too, even though it changes nothing about the numbers. It is read when
            // the tuner runs, so without this, turning logging on produced no logging until
            // something else happened to trigger a pass. That is the state somebody is in
            // when they are checking whether live editing works at all, and finding silence
            // is what makes them conclude it does not.
            SurgeConfig.Verbose.SettingChanged += Queue;

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
        /// Reports the local player's effective max adrenaline whenever it changes.
        ///
        /// Added because every check up to now was made against this mod's own log, which
        /// only ever proved that the value was written to the prefab. Whether the number the
        /// game actually uses then followed was assumed rather than measured, and a player
        /// reporting that nothing changes is exactly the case that assumption cannot answer.
        ///
        /// This is the number the bar is drawn from and the threshold the trinket fires at:
        /// Player.GetMaxAdrenaline, which is the player's own base plus the sum of the
        /// equipment modifiers. Equip a trinket and it should appear. Edit the config and it
        /// should change within a second or two, with no restart. If it does not move, the
        /// fault is between the prefab and the player rather than in reading the file.
        /// </summary>
        private void ReportLiveMax(Player player)
        {
            if (!SurgeConfig.Verbose.Value) return;

            var live = player.GetMaxAdrenaline();
            if (Mathf.Approximately(live, _lastReportedMax)) return;

            _lastReportedMax = live;
            Log.LogInfo("Local player's max adrenaline is now "
                        + live.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                        + " (0 means no trinket equipped).");
        }

        /// <summary>
        /// Reports what the equipped trinket's own shared data says, which is the one number
        /// that separates the two remaining explanations.
        ///
        /// A retune writes to the prefab in ObjectDB. Inventory.AddItem builds an item with
        /// ItemData.Clone, which is a MemberwiseClone and therefore copies the *reference* to
        /// m_shared, so on paper the equipped item and the prefab are the same object and a
        /// write to one is a write to both. Measured, they are not behaving that way: the
        /// prefab went to 99 and Player.GetMaxAdrenaline stayed at 35.
        ///
        /// So either the equipped item is holding its own copy of SharedData, in which case
        /// this prints the old value and the fix is to write through the player's inventory
        /// as well as the prefab, or it is holding the prefab's and this prints the new value,
        /// in which case the fault is downstream in the equipment modifier totals and the fix
        /// is to make the game recompute them. Guessing between those has already cost three
        /// wrong answers, so it gets measured.
        /// </summary>
        private void ReportEquippedShared(Player player)
        {
            if (!SurgeConfig.Verbose.Value) return;

            var inventory = player.GetInventory();
            if (inventory == null) return;

            foreach (var item in inventory.GetAllItems())
            {
                if (item == null || !item.m_equipped || item.m_shared == null) continue;
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Trinket) continue;

                if (Mathf.Approximately(item.m_shared.m_maxAdrenaline, _lastEquippedShared)) return;

                _lastEquippedShared = item.m_shared.m_maxAdrenaline;
                Log.LogInfo("Equipped trinket's own shared max is now "
                            + _lastEquippedShared.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                            + ". If this tracks the retune but the line above does not, the item"
                            + " is fine and the equipment totals are stale.");
                return;
            }
        }

        /// <summary>
        /// Joins Core's version gate when Core is installed, and does nothing when it is not.
        ///
        /// Surge is the one mod in this set that has to stand alone, because it is the one
        /// worth handing to somebody who wants only it. Nothing here needs Core: no prefab is
        /// registered, no ZDO is written, no RPC is sent, and the max is computed on the
        /// owning client and never travels. Requiring Core would mean a stranger installing
        /// two mods to get one, and a hard dependency that is missing does not degrade - the
        /// plugin simply never loads.
        ///
        /// So the reference is compile-time only and the call is made behind a check. What is
        /// kept when Core *is* present is the honest reason for registering at all: two
        /// players in the same fight would earn their trinket procs at different rates with
        /// neither of them being told, and a silent disagreement is what the gate exists to
        /// report. That is fairness rather than safety - unlike the mods that register
        /// prefabs, nothing here corrupts when only one side has it, which is exactly why it
        /// is safe to make optional.
        /// </summary>
        private void TryRegisterWithCore()
        {
            if (!Chainloader.PluginInfos.ContainsKey(CoreGuid))
            {
                Log.LogInfo("Core not installed - running standalone, without the version gate.");
                return;
            }

            RegisterWithCore();
        }

        /// <summary>
        /// Kept separate and never inlined on purpose. The JIT resolves the assemblies a
        /// method needs when it first compiles that method, so a Suite call sitting directly
        /// in Awake would drag Ezomic.Core in before the check above could prevent it - and
        /// the missing-assembly exception would land during plugin load, which is the failure
        /// this whole arrangement exists to avoid. Isolating it means the type is only ever
        /// resolved on a machine that has Core.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterWithCore()
        {
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config);
        }

        /// <summary>
        /// The one place anything actually happens, because it is the one place guaranteed
        /// to be the main thread.
        /// </summary>
        private void Update()
        {
            // unscaledTime, not time. Time.time is scaled by timeScale, and a singleplayer
            // game is paused while the player is alt-tabbed out editing the config - which is
            // precisely when this clock needs to run. On scaled time the delay never elapsed
            // until they came back and unpaused, so the reload arrived after the moment they
            // were looking for it.
            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollSeconds;
                ConfigWatcher.Poll();

                // The safety net, and the reason this mod stopped depending on being told.
                //
                // A player reported that config changes only took effect after reloading the
                // world, on a build where reading the file, noticing the edit and applying it
                // had each been verified here. Two fixes aimed at that failed to help him,
                // which is the point at which guessing at someone else's machine stops being
                // worth another round.
                //
                // So the event is now an optimisation rather than the mechanism. This
                // recomputes every trinket from its captured original once a second and
                // writes only what differs, which is a dictionary lookup and a float compare
                // per item. Whatever breaks the notification - a watcher that does not fire,
                // a config editor that sets values without raising the event, something in a
                // mod manager's profile layout - the values are correct within a second
                // regardless, because nothing has to arrive for this to run.
                AdrenalineTuner.Apply(announce: false);
            }

            if (ConfigWatcher.TakeDirty()) _reloadAt = Time.unscaledTime + SettleSeconds;

            if (_reloadAt > 0f && Time.unscaledTime >= _reloadAt)
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

            ReportLiveMax(player);
            ReportEquippedShared(player);

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

using System;
using System.IO;
using BepInEx.Configuration;

namespace Surge
{
    /// <summary>
    /// Notices the .cfg being edited on disk and asks for a reload.
    ///
    /// Without this the mod's one interesting property is unreachable. Every value here
    /// feeds a number the game re-reads every frame, so a change genuinely can land live -
    /// but BepInEx 5 does not watch config files, and nothing in this profile provides an
    /// in-game config editor. So the only way to try a different multiplier was to edit the
    /// file and restart the game, which is a full load per value and turns balancing a
    /// number into an afternoon.
    ///
    /// The watcher fires on a background thread and Unity objects may only be touched on
    /// the main one, so nothing is done here beyond setting a flag. The plugin's Update
    /// picks it up, waits for the writes to stop, and does the actual work.
    /// </summary>
    internal static class ConfigWatcher
    {
        private static FileSystemWatcher _watcher;

        /// <summary>
        /// Set from the watcher thread, read and cleared from Update. Volatile because
        /// those are genuinely two threads and the value is written by one and polled by
        /// the other.
        /// </summary>
        private static volatile bool _dirty;

        public static void Start(ConfigFile config)
        {
            var path = config.ConfigFilePath;
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            try
            {
                _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
                {
                    // Editors vary in how they land a save. Some write in place, some write
                    // a temporary file and rename it over the original - which arrives as a
                    // create or a rename rather than a change, so all four are wanted.
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };

                _watcher.Changed += OnTouched;
                _watcher.Created += OnTouched;
                _watcher.Renamed += OnTouched;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                // Not fatal, and not worth failing to load over. The mod still works; it
                // just needs a restart to pick up an edit, which is where it started.
                SurgePlugin.Log.LogWarning("Could not watch the config file, so edits will "
                                           + "need a restart: " + e.Message);
                _watcher = null;
            }
        }

        public static void Stop()
        {
            if (_watcher == null) return;

            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }

        private static void OnTouched(object sender, FileSystemEventArgs e)
        {
            _dirty = true;
        }

        /// <summary>
        /// True once per burst of edits. Clearing on read is deliberate: the caller adds its
        /// own settle time, and a save that arrives as three events should still be one
        /// reload.
        /// </summary>
        public static bool TakeDirty()
        {
            if (!_dirty) return false;

            _dirty = false;
            return true;
        }
    }
}

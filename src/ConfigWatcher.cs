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

        /// <summary>
        /// The file, and the write time last seen on it. Kept because the watcher cannot be
        /// trusted on its own - see Poll.
        /// </summary>
        private static string _path;
        private static DateTime _stamp;

        public static void Start(ConfigFile config)
        {
            var path = config.ConfigFilePath;
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            // Seeded now, after Bind has written the file, so the first poll compares against
            // what is already on disk rather than firing a reload for no reason.
            _path = path;
            _stamp = SafeStamp(path);

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
                // Not fatal. Poll covers this case, so an edit is still picked up; it just
                // arrives on the next poll rather than immediately.
                SurgePlugin.Log.LogWarning("Could not watch the config file, so edits will be "
                                           + "noticed by polling instead: " + e.Message);
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
        /// Checks the file's write time. Called about once a second from Update, and it is
        /// what actually makes live editing dependable rather than an optimistic claim.
        ///
        /// A FileSystemWatcher looked sufficient because it works here, and a player reported
        /// that edits only took effect after a restart. The watcher is the part that differs
        /// between machines: it runs on Unity's Mono rather than desktop .NET, and mod
        /// managers commonly place a profile behind a junction or a symlink, which the
        /// watcher does not see through - events raised against the real path never reach a
        /// watch registered on the link. None of that is detectable from in here, and all of
        /// it is invisible to the person it happens to, who simply concludes the feature does
        /// not work.
        ///
        /// Comparing a timestamp has none of those failure modes and costs one stat a second.
        /// So the watcher is kept only as an accelerator, and this is the guarantee.
        /// </summary>
        public static void Poll()
        {
            if (_path == null) return;

            var stamp = SafeStamp(_path);

            // MinValue means the read failed, usually because a save is in flight. Ignored
            // rather than treated as a change; the next poll a second later sees the result.
            if (stamp == DateTime.MinValue || stamp == _stamp) return;

            _stamp = stamp;
            _dirty = true;
        }

        private static DateTime SafeStamp(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
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

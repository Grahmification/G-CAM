using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Tooling.Import;

namespace GCam.Core.Tooling
{
    /// <summary>
    /// The set of tool libraries open for editing, and which of them have unsaved changes.
    /// </summary>
    /// <remarks>
    /// Changes to an EXISTING library are held in memory until <see cref="SaveAll"/>.
    /// That is what lets the browser offer a single OK that commits everything and a
    /// Cancel that discards it, including changes to a library the user navigated away
    /// from.
    ///
    /// Creating a library is different: <see cref="CreateNew"/> and
    /// <see cref="SaveAsCopy"/> write the file straight away. Creating a file is an
    /// explicit act with a path the user just chose, not an edit to be weighed up - and
    /// a library that exists only in memory cannot be seen in the folder tree, which is
    /// where the user expects to find what they just made.
    ///
    /// In Core rather than the viewmodel because "which libraries are dirty and what
    /// happens when you commit" is a rule, not presentation, and it is worth testing
    /// without a window.
    /// </remarks>
    public sealed class LibrarySession
    {
        private readonly ToolLibraryImporter _importer;
        private readonly IGCamLog _log;

        private readonly Dictionary<string, ToolLibrary> _open =
            new Dictionary<string, ToolLibrary>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _dirty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public LibrarySession(ToolLibraryImporter importer = null, IGCamLog log = null)
        {
            _importer = importer ?? new ToolLibraryImporter();
            _log = log ?? NullLog.Instance;
        }

        /// <summary>Paths of libraries with unsaved changes.</summary>
        public IReadOnlyList<string> DirtyPaths => _dirty.ToList();

        public bool HasUnsavedChanges => _dirty.Count > 0;

        /// <summary>Libraries open in this session that can be written to.</summary>
        public IEnumerable<ToolLibrary> WritableOpenLibraries =>
            _open.Where(kv => CanEdit(kv.Key)).Select(kv => kv.Value);

        /// <summary>
        /// Whether a library at this path can be edited in place.
        /// </summary>
        /// <remarks>
        /// Only G-CAM's own format. An .hsmlib can be read but not written - there is no
        /// HSMWorks writer, and inventing one risks corrupting a file another application
        /// owns. Editing an imported library means saving a G-CAM copy first.
        /// </remarks>
        public bool CanEdit(string path) => _importer.CanWrite(path);

        public bool IsDirty(string path) => _dirty.Contains(path);

        /// <summary>
        /// Returns the library at <paramref name="path"/>, loading it on first use and
        /// keeping it thereafter so edits survive navigating away and back.
        /// </summary>
        public ToolLibraryReadResult Open(string path)
        {
            ToolLibrary cached;
            if (_open.TryGetValue(path, out cached))
            {
                return new ToolLibraryReadResult(cached, new List<string>());
            }

            IToolLibraryReader reader = _importer.ReaderFor(path);
            ToolLibraryReadResult result;

            using (FileStream stream = File.OpenRead(path))
            {
                result = reader.Read(stream, path);
            }

            _open[path] = result.Library;
            return result;
        }

        /// <summary>Records that a library has changed and needs writing on commit.</summary>
        public void MarkDirty(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                _dirty.Add(path);
            }
        }

        /// <summary>
        /// Creates an empty library and writes it to <paramref name="path"/> at once.
        /// </summary>
        /// <remarks>
        /// Written immediately, and therefore NOT dirty: the file exists from this moment
        /// and appears in the folder tree. Tools added to it afterwards are ordinary
        /// edits and wait for the session commit like any other.
        /// </remarks>
        /// <exception cref="GCamUserException">The file could not be written.</exception>
        public ToolLibrary CreateNew(string path, string name)
        {
            var library = new ToolLibrary
            {
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name,
                SourcePath = path,
            };

            Write(library, path);
            _open[path] = library;
            return library;
        }

        /// <summary>
        /// Copies a library to a new path and writes it - how an imported .hsmlib becomes
        /// an editable G-CAM library.
        /// </summary>
        /// <remarks>
        /// Written immediately for the same reason as <see cref="CreateNew"/>, and left
        /// clean. The copy is fully independent: holders are cloned and the copied tools
        /// re-pointed at them, so editing one library cannot reach into the other.
        /// </remarks>
        /// <exception cref="GCamUserException">The file could not be written.</exception>
        public ToolLibrary SaveAsCopy(ToolLibrary source, string newPath, string name)
        {
            var copy = new ToolLibrary
            {
                Name = name ?? source.Name,
                SourcePath = newPath,
            };

            foreach (Holder holder in source.Holders)
            {
                copy.Holders.Add(holder.Clone());
            }

            foreach (Tool tool in source.Tools)
            {
                Tool cloned = tool.Clone();

                // Re-point holders at the copies, or the two libraries would share them.
                if (cloned.Holder != null)
                {
                    cloned.Holder = copy.FindHolderById(cloned.Holder.Id) ?? cloned.Holder;
                }

                copy.Tools.Add(cloned);
            }

            Write(copy, newPath);
            _open[newPath] = copy;
            return copy;
        }

        /// <summary>
        /// Writes every dirty library.
        /// </summary>
        /// <returns>
        /// Paths that could not be written, with the reason. Empty on success. Libraries
        /// that saved stay saved even if a later one fails - a partial commit is better
        /// than discarding work because one path was read-only.
        /// </returns>
        public IReadOnlyList<string> SaveAll()
        {
            var failures = new List<string>();

            foreach (string path in _dirty.ToList())
            {
                ToolLibrary library;
                if (!_open.TryGetValue(path, out library))
                {
                    continue;
                }

                try
                {
                    Write(library, path);
                    _dirty.Remove(path);
                }
                catch (GCamUserException ex)
                {
                    // One unwritable path must not abandon the libraries that did save.
                    failures.Add(ex.Message);
                }
            }

            return failures;
        }

        /// <summary>
        /// Writes a library to disk.
        /// </summary>
        /// <remarks>
        /// Writes to a temporary file and swaps, so a failure part-way through cannot
        /// leave a half-written library where a good one used to be.
        /// </remarks>
        /// <exception cref="GCamUserException">The file could not be written.</exception>
        private void Write(ToolLibrary library, string path)
        {
            string temporary = path + ".saving";

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (FileStream stream = File.Create(temporary))
                {
                    new GcamXmlLibraryWriter().Write(library, stream);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
                _log.Info("Saved tool library {0}", path);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Could not save tool library {0}", path);

                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (Exception)
                {
                    // Leaving a .saving file behind is untidy, not harmful.
                }

                throw new GCamUserException(
                    $"Could not save '{Path.GetFileName(path)}': {ex.Message}", ex);
            }
        }

        /// <summary>Throws away every unsaved change and forgets the loaded libraries.</summary>
        public void DiscardAll()
        {
            _open.Clear();
            _dirty.Clear();
        }
    }
}

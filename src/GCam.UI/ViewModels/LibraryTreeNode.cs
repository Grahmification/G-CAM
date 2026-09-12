using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace GCam.UI.ViewModels
{
    /// <summary>Shared behaviour for nodes in the library tree.</summary>
    public abstract class LibraryTreeNode : ViewModelBase
    {
        private bool _isExpanded;
        private bool _isSelected;

        protected LibraryTreeNode(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public ObservableCollection<LibraryTreeNode> Children { get; } =
            new ObservableCollection<LibraryTreeNode>();

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }

    /// <summary>
    /// A folder being watched, or a sub-folder inside one.
    /// </summary>
    /// <remarks>
    /// Only root nodes can be removed - a sub-folder appears because its parent is
    /// watched, so removing it on its own would be meaningless.
    /// </remarks>
    public sealed class FolderNode : LibraryTreeNode
    {
        /// <summary>Extensions the browser recognises as tool libraries.</summary>
        private static readonly string[] LibraryExtensions = { ".gcamtools", ".hsmlib" };

        public FolderNode(string path, bool isRoot)
            : base(isRoot ? path : Path.GetFileName(path))
        {
            FullPath = path;
            IsRoot = isRoot;
            IsExpanded = isRoot;
        }

        public string FullPath { get; }

        public bool IsRoot { get; }

        /// <summary>True when the folder no longer exists on disk.</summary>
        public bool IsMissing { get; private set; }

        /// <summary>
        /// Rebuilds this node's children from disk.
        /// </summary>
        /// <remarks>
        /// Sub-folders are included recursively, but only when they contain a library
        /// somewhere beneath - otherwise watching a folder near the root of a drive
        /// would produce an unusable tree.
        ///
        /// Folders that cannot be read are skipped rather than throwing: a watched path
        /// may be an unavailable network share, and the rest of the tree should still work.
        /// </remarks>
        public void Refresh()
        {
            Children.Clear();
            IsMissing = !SafeExists(FullPath);
            Raise(nameof(IsMissing));

            if (IsMissing)
            {
                return;
            }

            foreach (string directory in SafeDirectories(FullPath).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!ContainsLibrary(directory))
                {
                    continue;
                }

                var child = new FolderNode(directory, isRoot: false);
                child.Refresh();
                Children.Add(child);
            }

            foreach (string file in LibraryFiles(FullPath).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                Children.Add(new LibraryFileNode(file));
            }
        }

        private static bool ContainsLibrary(string directory)
        {
            if (LibraryFiles(directory).Any())
            {
                return true;
            }

            return SafeDirectories(directory).Any(ContainsLibrary);
        }

        private static IEnumerable<string> LibraryFiles(string directory)
        {
            try
            {
                return Directory.GetFiles(directory)
                                .Where(f => LibraryExtensions.Contains(
                                    Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                                .ToList();
            }
            catch (Exception)
            {
                return Enumerable.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeDirectories(string directory)
        {
            try
            {
                return Directory.GetDirectories(directory);
            }
            catch (Exception)
            {
                return Enumerable.Empty<string>();
            }
        }

        private static bool SafeExists(string directory)
        {
            try
            {
                return Directory.Exists(directory);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>A tool library file. Selecting one loads its tools into the list.</summary>
    public sealed class LibraryFileNode : LibraryTreeNode
    {
        public LibraryFileNode(string path)
            : base(Path.GetFileNameWithoutExtension(path))
        {
            FullPath = path;
        }

        public string FullPath { get; }

        /// <summary>Extension without the dot, shown as a badge so formats are distinguishable.</summary>
        public string Format => Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();
    }
}

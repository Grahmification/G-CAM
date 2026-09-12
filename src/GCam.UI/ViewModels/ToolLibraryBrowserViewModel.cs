using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using GCam.Core.Diagnostics;
using GCam.Core.Settings;
using GCam.Core.Tooling;
using GCam.Core.Tooling.Import;

namespace GCam.UI.ViewModels
{
    /// <summary>
    /// Drives the tool library browser: watched folders on the left, the selected
    /// library's tools in the middle, the selected tool's profile below the tree.
    /// </summary>
    public sealed class ToolLibraryBrowserViewModel : ViewModelBase
    {
        private readonly IGCamSettings _settings;
        private readonly ToolLibraryImporter _importer;
        private readonly IGCamLog _log;

        private readonly ObservableCollection<Tool> _tools = new ObservableCollection<Tool>();
        private readonly ICollectionView _toolsView;

        private LibraryFileNode _selectedLibrary;
        private Tool _selectedTool;
        private string _searchText = string.Empty;
        private string _status = "Add a folder to get started.";

        public ToolLibraryBrowserViewModel(
            IGCamSettings settings, ToolLibraryImporter importer = null, IGCamLog log = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _importer = importer ?? new ToolLibraryImporter();
            _log = log ?? NullLog.Instance;

            _toolsView = CollectionViewSource.GetDefaultView(_tools);
            _toolsView.Filter = o => ToolSearch.Matches(o as Tool, _searchText);

            LoadFolders();
        }

        /// <summary>Watched folders, each the root of a tree of libraries.</summary>
        public ObservableCollection<LibraryTreeNode> Folders { get; } =
            new ObservableCollection<LibraryTreeNode>();

        /// <summary>Tools in the selected library, filtered by <see cref="SearchText"/>.</summary>
        public ICollectionView Tools => _toolsView;

        /// <summary>File extensions the browser can open, for the folder-picker hint.</summary>
        public string SupportedFormats =>
            string.Join(", ", _importer.Readers.Select(r => "*" + r.FileExtension));

        public LibraryFileNode SelectedLibrary
        {
            get => _selectedLibrary;
            set
            {
                if (Set(ref _selectedLibrary, value))
                {
                    LoadSelectedLibrary();
                }
            }
        }

        public Tool SelectedTool
        {
            get => _selectedTool;
            set => Set(ref _selectedTool, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (Set(ref _searchText, value ?? string.Empty))
                {
                    _toolsView.Refresh();
                    Raise(nameof(ToolCountSummary));
                }
            }
        }

        /// <summary>One-line description of what is on screen, shown in the status strip.</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        public string ToolCountSummary
        {
            get
            {
                if (_tools.Count == 0)
                {
                    return string.Empty;
                }

                int shown = _toolsView.Cast<object>().Count();
                return shown == _tools.Count
                    ? $"{_tools.Count} tools"
                    : $"{shown} of {_tools.Count} tools";
            }
        }

        /// <summary>Adds a folder to watch and saves the list.</summary>
        public void AddFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (_settings.ToolLibraryFolders.Any(
                    f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)))
            {
                Status = "That folder is already in the list.";
                return;
            }

            _settings.ToolLibraryFolders.Add(path);
            _settings.Save();
            LoadFolders();
            Status = "Added " + path;
        }

        /// <summary>Stops watching a root folder. Only roots can be removed.</summary>
        public void RemoveFolder(FolderNode node)
        {
            if (node == null || !node.IsRoot)
            {
                return;
            }

            string match = _settings.ToolLibraryFolders.FirstOrDefault(
                f => string.Equals(f, node.FullPath, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                return;
            }

            _settings.ToolLibraryFolders.Remove(match);
            _settings.Save();

            // Clear the list if the library on screen came from the folder being removed.
            if (_selectedLibrary != null &&
                _selectedLibrary.FullPath.StartsWith(node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                SelectedLibrary = null;
            }

            LoadFolders();
            Status = "Removed " + node.FullPath;
        }

        /// <summary>Re-scans every watched folder, picking up files added outside G-CAM.</summary>
        public void Refresh()
        {
            string previous = _selectedLibrary?.FullPath;

            LoadFolders();

            if (previous != null)
            {
                LibraryFileNode restored = AllNodes(Folders)
                    .OfType<LibraryFileNode>()
                    .FirstOrDefault(n => string.Equals(n.FullPath, previous, StringComparison.OrdinalIgnoreCase));

                if (restored != null)
                {
                    SelectedLibrary = restored;
                    restored.IsSelected = true;
                }
            }

            Status = "Refreshed.";
        }

        private void LoadFolders()
        {
            Folders.Clear();

            foreach (string path in _settings.ToolLibraryFolders)
            {
                var node = new FolderNode(path, isRoot: true);
                node.Refresh();
                Folders.Add(node);
            }

            if (Folders.Count == 0)
            {
                Status = "Add a folder to get started.";
            }
        }

        private void LoadSelectedLibrary()
        {
            _tools.Clear();
            SelectedTool = null;

            if (_selectedLibrary == null)
            {
                Status = "No library selected.";
                RaiseListSummary();
                return;
            }

            try
            {
                IToolLibraryReader reader = _importer.ReaderFor(_selectedLibrary.FullPath);
                ToolLibraryReadResult result;

                using (FileStream stream = File.OpenRead(_selectedLibrary.FullPath))
                {
                    result = reader.Read(stream, _selectedLibrary.FullPath);
                }

                foreach (Tool tool in result.Library.Tools)
                {
                    _tools.Add(tool);
                }

                Status = result.Warnings.Count == 0
                    ? $"{Path.GetFileName(_selectedLibrary.FullPath)} — {_tools.Count} tools"
                    : $"{Path.GetFileName(_selectedLibrary.FullPath)} — {_tools.Count} tools, " +
                      $"{result.Warnings.Count} skipped: {result.Warnings[0]}";

                foreach (string warning in result.Warnings)
                {
                    _log.Warn("Tool library import: {0}", warning);
                }
            }
            catch (GCamUserException ex)
            {
                // A library that will not open is normal - a partial file, an unexpected
                // format. Say so in the status strip rather than raising a dialog over it.
                Status = ex.Message;
                _log.Info("Could not open {0}: {1}", _selectedLibrary.FullPath, ex.Message);
            }
            catch (Exception ex)
            {
                Status = "Could not open this library. See the log for details.";
                _log.Error(ex, "Unexpected failure opening {0}", _selectedLibrary.FullPath);
            }

            RaiseListSummary();
        }

        private void RaiseListSummary()
        {
            _toolsView.Refresh();
            Raise(nameof(ToolCountSummary));
        }

        private static IEnumerable<LibraryTreeNode> AllNodes(IEnumerable<LibraryTreeNode> nodes)
        {
            foreach (LibraryTreeNode node in nodes)
            {
                yield return node;

                foreach (LibraryTreeNode child in AllNodes(node.Children))
                {
                    yield return child;
                }
            }
        }
    }
}

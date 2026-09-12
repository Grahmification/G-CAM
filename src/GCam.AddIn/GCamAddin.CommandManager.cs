using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GCam.AddIn.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GCam.AddIn
{
    /// <summary>
    /// Builds the G-CAM toolbar, menu and CommandManager tab. UI construction only -
    /// the callbacks live in GCamAddin.Callbacks.cs.
    /// </summary>
    public partial class GCamAddin
    {
        // Must be unique among all add-ins and stable across releases. Per the API
        // docs, changing a CommandGroup's contents without changing this ID leaves
        // users with a stale cached toolbar, so bump it if the buttons change.
        private const int CommandGroupId = 4620;

        private const string TabName = "G-CAM";

        // Sizes SOLIDWORKS accepts for icon lists. Each file must exist for every size.
        private static readonly int[] IconSizes = { 20, 32, 40, 64, 96, 128 };

        private ICommandGroup _cmdGroup;
        private int[] _commandIds;

        private void BuildCommandManager()
        {
            // Tells SOLIDWORKS which object carries the callback methods named below.
            _swApp.SetAddinCallbackInfo2(0, this, _addinID);

            int errors = 0;
            // IgnorePreviousVersion: true discards any toolbar layout cached from an
            // earlier build. Convenient while the button set is still changing.
            _cmdGroup = _iCmdMgr.CreateCommandGroup2(
                CommandGroupId, TabName, "G-CAM toolpath tools", "G-CAM", -1, true, ref errors);

            if (_cmdGroup == null)
            {
                return;
            }

            _cmdGroup.IconList = IconPaths("buttons");
            _cmdGroup.MainIconList = IconPaths("main");

            AddButton(GCamCommand.NewJob, "New Job", "Create a CAM job for this part");
            AddButton(GCamCommand.ToolLibrary, "Tool Library", "Open the tool library");
            AddButton(GCamCommand.PostProcess, "Post Process", "Post toolpaths to G-code");
            AddButton(GCamCommand.Simulate, "Simulate", "Simulate material removal");

            _cmdGroup.HasMenu = true;
            _cmdGroup.HasToolbar = true;
            _cmdGroup.Activate();

            AddCommandTab();
        }

        private void AddButton(GCamCommand command, string title, string hint)
        {
            // The callback strings are resolved by name at click time, so a typo here
            // fails silently at runtime rather than at compile time. nameof() keeps
            // them honest.
            _cmdGroup.AddCommandItem2(
                title,
                -1,
                hint,
                title,
                (int)command,
                $"{nameof(OnCommand)}({(int)command})",
                $"{nameof(OnCommandEnable)}({(int)command})",
                (int)command,
                (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem);
        }

        /// <summary>
        /// Adds the CommandManager ribbon tab for part documents.
        /// </summary>
        private void AddCommandTab()
        {
            const int docType = (int)swDocumentTypes_e.swDocPART;

            // Remove any tab left behind by a previous build before adding ours,
            // otherwise repeated debug sessions stack up duplicates.
            CommandTab existing = _iCmdMgr.GetCommandTab(docType, TabName);
            if (existing != null)
            {
                _iCmdMgr.RemoveCommandTab(existing);
            }

            CommandTab tab = _iCmdMgr.AddCommandTab(docType, TabName);
            if (tab == null)
            {
                return;
            }

            CommandTabBox box = tab.AddCommandTabBox();
            if (box == null)
            {
                return;
            }

            _commandIds = Enum.GetValues(typeof(GCamCommand))
                              .Cast<GCamCommand>()
                              .Select(c => _cmdGroup.CommandID[(int)c])
                              .ToArray();

            int[] styles = _commandIds
                .Select(_ => (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow)
                .ToArray();

            box.AddCommands(_commandIds, styles);
        }

        private void RemoveCommandManager()
        {
            if (_iCmdMgr == null)
            {
                return;
            }

            CommandTab tab = _iCmdMgr.GetCommandTab((int)swDocumentTypes_e.swDocPART, TabName);
            if (tab != null)
            {
                _iCmdMgr.RemoveCommandTab(tab);
            }

            _iCmdMgr.RemoveCommandGroup2(CommandGroupId, true);
            _cmdGroup = null;
        }

        /// <summary>
        /// Absolute paths to the icon files, one per size. SOLIDWORKS reads these
        /// from disk at load time, so they ship next to the assembly rather than as
        /// embedded resources.
        /// </summary>
        private static string[] IconPaths(string prefix)
        {
            string dir = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "Resources", "icons");

            var paths = new List<string>();
            foreach (int size in IconSizes)
            {
                string file = Path.Combine(dir, $"{prefix}{size}.png");
                if (File.Exists(file))
                {
                    paths.Add(file);
                }
            }

            return paths.ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GCam.Core.Diagnostics;

namespace GCam.Core.Settings
{
    /// <summary>
    /// Settings stored as XML under %LOCALAPPDATA%\G-CAM.
    /// </summary>
    /// <remarks>
    /// Lives in Core rather than the add-in because none of it touches SOLIDWORKS, and
    /// keeping it here means the tests can exercise it against a temp directory.
    ///
    /// Settings are a convenience, never a prerequisite: a missing, unreadable or
    /// corrupt file leaves G-CAM running with defaults rather than failing to start.
    /// That is why nothing here throws.
    /// </remarks>
    public sealed class XmlSettingsStore : IGCamSettings
    {
        public const int CurrentVersion = 1;

        private const string RootElement = "gcamSettings";

        private readonly string _path;
        private readonly IGCamLog _log;

        public XmlSettingsStore(string path, IGCamLog log = null)
        {
            _path = path;
            _log = log ?? NullLog.Instance;
        }

        /// <summary>%LOCALAPPDATA%\G-CAM\settings.xml - beside the logs.</summary>
        public static string DefaultPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "G-CAM",
                "settings.xml");

        public IList<string> ToolLibraryFolders { get; } = new List<string>();

        /// <summary>
        /// Reads settings from <paramref name="path"/>, or returns empty defaults if it
        /// cannot be read for any reason.
        /// </summary>
        public static XmlSettingsStore Load(string path = null, IGCamLog log = null)
        {
            path = path ?? DefaultPath;
            var store = new XmlSettingsStore(path, log);

            try
            {
                if (!File.Exists(path))
                {
                    return store;
                }

                XDocument document = XDocument.Load(path);
                XElement root = document.Root;
                if (root == null || root.Name.LocalName != RootElement)
                {
                    store._log.Warn("Ignoring {0}: not a G-CAM settings file.", path);
                    return store;
                }

                foreach (XElement folder in root.Elements("toolLibraryFolders").Elements("folder"))
                {
                    string value = (string)folder.Attribute("path");
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        store.ToolLibraryFolders.Add(value);
                    }
                }
            }
            catch (Exception ex)
            {
                // Losing preferences is an annoyance; refusing to start is not acceptable.
                store._log.Error(ex, "Could not read settings from {0}; using defaults.", path);
                store.ToolLibraryFolders.Clear();
            }

            return store;
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var document = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XElement(
                        RootElement,
                        new XAttribute("version", CurrentVersion),
                        new XElement(
                            "toolLibraryFolders",
                            ToolLibraryFolders
                                .Where(f => !string.IsNullOrWhiteSpace(f))
                                .Select(f => new XElement("folder", new XAttribute("path", f))))));

                document.Save(_path);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Could not save settings to {0}.", _path);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GCam.Core.Tooling.Import
{
    /// <summary>What an import did, in terms a user can check against their file.</summary>
    public sealed class ImportSummary
    {
        public int Added { get; set; }

        public int Updated { get; set; }

        public int HoldersAdded { get; set; }

        /// <summary>Tools that did not come across, one line each explaining why.</summary>
        public List<string> Skipped { get; } = new List<string>();

        public int SkippedCount => Skipped.Count;

        /// <summary>A short report suitable for showing in a dialog.</summary>
        public string Describe(string fileName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Imported {fileName}");
            sb.AppendLine();
            sb.AppendLine($"  Added    {Added}");
            sb.AppendLine($"  Updated  {Updated}");
            sb.AppendLine($"  Skipped  {SkippedCount}");

            if (Skipped.Count > 0)
            {
                sb.AppendLine();
                foreach (string reason in Skipped)
                {
                    sb.AppendLine("  • " + reason);
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Merges a library read from a file into one already open.
    /// </summary>
    /// <remarks>
    /// Tools are matched by id. Since importers reuse the source format's own guid as
    /// the G-CAM id, re-importing the same file updates the tools it brought in last
    /// time rather than creating duplicates - which is what makes it safe to re-import
    /// after editing the source library.
    /// </remarks>
    public sealed class ToolLibraryImporter
    {
        private readonly IReadOnlyList<IToolLibraryReader> _readers;

        public ToolLibraryImporter()
            : this(new IToolLibraryReader[] { new GcamXmlLibraryReader(), new HsmLibraryReader() })
        {
        }

        public ToolLibraryImporter(IReadOnlyList<IToolLibraryReader> readers)
        {
            _readers = readers;
        }

        /// <summary>Readers available, for building a file dialog filter.</summary>
        public IReadOnlyList<IToolLibraryReader> Readers => _readers;

        /// <summary>
        /// Picks a reader by file extension.
        /// </summary>
        /// <exception cref="Diagnostics.GCamUserException">No reader handles it.</exception>
        public IToolLibraryReader ReaderFor(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            IToolLibraryReader reader = _readers.FirstOrDefault(
                r => string.Equals(r.FileExtension, extension, StringComparison.OrdinalIgnoreCase));

            if (reader == null)
            {
                throw new Diagnostics.GCamUserException(
                    $"G-CAM does not know how to read '{extension}' tool libraries. Supported: " +
                    string.Join(", ", _readers.Select(r => r.FileExtension)) + ".");
            }

            return reader;
        }

        /// <summary>
        /// Whether G-CAM can write this path, as opposed to only read it.
        /// </summary>
        /// <remarks>
        /// Only the native format. There is no HSMWorks writer, so an imported .hsmlib
        /// is read-only and has to be saved as a G-CAM library before it can be edited.
        /// </remarks>
        public bool CanWrite(string path)
        {
            return string.Equals(
                Path.GetExtension(path ?? string.Empty),
                GcamXmlLibrary.FileExtension,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Reads <paramref name="path"/> and merges it into <paramref name="target"/>.</summary>
        public ImportSummary ImportFile(ToolLibrary target, string path)
        {
            IToolLibraryReader reader = ReaderFor(path);
            using (FileStream stream = File.OpenRead(path))
            {
                return Import(target, reader.Read(stream, path));
            }
        }

        /// <summary>Merges an already-read library into <paramref name="target"/>.</summary>
        public ImportSummary Import(ToolLibrary target, ToolLibraryReadResult source)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var summary = new ImportSummary();
            summary.Skipped.AddRange(source.Warnings);

            foreach (Holder holder in source.Library.Holders)
            {
                if (target.FindHolderById(holder.Id) == null)
                {
                    target.Holders.Add(holder);
                    summary.HoldersAdded++;
                }
            }

            foreach (Tool tool in source.Library.Tools)
            {
                Tool existing = target.FindById(tool.Id);
                if (existing == null)
                {
                    target.Tools.Add(tool);
                    summary.Added++;
                }
                else
                {
                    // Replace in place so anything holding the list index still sees the
                    // update, and so ordering does not shuffle on re-import.
                    target.Tools[target.Tools.IndexOf(existing)] = tool;
                    summary.Updated++;
                }
            }

            return summary;
        }
    }
}

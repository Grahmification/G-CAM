using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.Core.Persistence;
using GCam.Core.Strategies;
using GCam.Core.Tooling;
using GCam.SolidWorks.Persistence.Interop;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Persistence
{
    /// <summary>
    /// Reads and writes a part's CAM data inside the SOLIDWORKS document itself.
    /// </summary>
    /// <remarks>
    /// This class knows about COM and storage nodes; it knows nothing about what the bytes
    /// mean, which is <see cref="GcamDocumentXml"/> and <see cref="ToolpathBinary"/> in
    /// Core. That split is what lets the format be round-tripped by a headless test and
    /// leaves only the plumbing here.
    ///
    /// The rules this has to obey, all from the 2025 help and recorded in
    /// docs/solidworks-api/third-party-storage.md:
    ///
    /// - **Writing is locked** until <c>SaveToStorageStoreNotify</c> has fired. There is no
    ///   flushing on demand; mark the document dirty and write when SOLIDWORKS asks.
    /// - **Reading is safe once a document is open**, not only during the notification,
    ///   which is why loading happens when the tab is built and does not race the event.
    /// - **Every get is matched by a release**, including when the get returns null, or the
    ///   node stays locked for the rest of the session.
    /// </remarks>
    public sealed class JobDocumentStorage
    {
        /// <summary>
        /// The storage node name, which is ours across every add-in in the process.
        /// </summary>
        /// <remarks>
        /// Under 30 characters, as the help requires, and specific enough that nobody
        /// else's add-in would pick it.
        /// </remarks>
        public const string StorageName = "ZaberGCamJobs";

        private readonly GcamDocumentXml _xml;
        private readonly IGCamLog _log;

        public JobDocumentStorage(StrategyCatalog catalog, IGCamLog log)
        {
            _xml = new GcamDocumentXml(catalog ?? throw new ArgumentNullException(nameof(catalog)));
            _log = log ?? NullLog.Instance;
        }

        /// <summary>
        /// Loads a part's jobs into <paramref name="target"/>.
        /// </summary>
        /// <returns>
        /// What was skipped or unreadable. Empty on a clean load, and on a part that has no
        /// G-CAM data at all - which is the ordinary case for every part made before now.
        /// </returns>
        /// <remarks>
        /// Loads into the existing document rather than handing back a new one, because the
        /// job tree's viewmodel is already bound to that instance. Expects it to be empty,
        /// which it is when a tab has just been built.
        /// </remarks>
        public IReadOnlyList<string> Load(ModelDoc2 model, JobDocument target)
        {
            if (model == null || target == null)
            {
                return new string[0];
            }

            if (target.Jobs.Count > 0)
            {
                _log.Warn("Loading CAM data into a document that already has jobs; ignoring.");
                return new string[0];
            }

            IStorage storage = null;

            try
            {
                storage = OpenStorage(model, storing: false);
                if (storage == null)
                {
                    // No G-CAM data in this part. Every part predating the add-in.
                    return new string[0];
                }

                byte[] xml = ComStreams.ReadStream(storage, GcamDocumentFormat.ModelStreamName);
                if (xml == null || xml.Length == 0)
                {
                    return new string[0];
                }

                DocumentReadResult result;
                using (var buffer = new MemoryStream(xml, writable: false))
                {
                    result = _xml.Read(buffer);
                }

                CopyInto(result.Document, target);
                var problems = new List<string>(result.Problems);

                LoadToolpaths(storage, result, problems);

                _log.Info(
                    "Loaded {0} job(s) and {1} tool(s) from the document.",
                    target.Jobs.Count, target.Tools.Count);

                return problems;
            }
            catch (GCamUserException ex)
            {
                // The part opens without its CAM data rather than not opening. Saying so
                // once is the caller's job; losing the part is not on the table.
                _log.Warn("This part's CAM data could not be read: {0}", ex.Message);
                return new[] { ex.Message };
            }
            finally
            {
                ReleaseStorage(model, storage);
            }
        }

        /// <summary>
        /// Writes a part's jobs into the document. Only legal while
        /// <c>SaveToStorageStoreNotify</c> is being handled.
        /// </summary>
        public void Save(ModelDoc2 model, JobDocument document)
        {
            if (model == null || document == null)
            {
                return;
            }

            IStorage storage = null;

            try
            {
                storage = OpenStorage(model, storing: true);
                if (storage == null)
                {
                    _log.Warn("SOLIDWORKS did not hand over a storage node; jobs were not saved.");
                    return;
                }

                byte[] xml;
                using (var buffer = new MemoryStream())
                {
                    _xml.Write(document, buffer);
                    xml = buffer.ToArray();
                }

                ComStreams.WriteStream(storage, GcamDocumentFormat.ModelStreamName, xml);

                // The same walk the XML used to name the streams, so the two cannot
                // disagree about which path belongs to which operation.
                foreach (ToolpathStreamEntry entry in GcamDocumentXml.ToolpathStreams(document))
                {
                    ComStreams.WriteStream(
                        storage, entry.StreamName, ToolpathBinary.ToBytes(entry.Operation.Toolpath));
                }

                storage.Commit(0);

                _log.Debug("Saved {0} job(s) into the document.", document.Jobs.Count);
            }
            finally
            {
                ReleaseStorage(model, storage);
            }
        }

        /// <summary>
        /// Tells SOLIDWORKS the document has unsaved changes, so closing it prompts.
        /// </summary>
        /// <remarks>
        /// The only way to get <c>SaveToStorageStoreNotify</c> to fire, and therefore the
        /// only way stored jobs ever reach the file. An edit that forgets to call this is
        /// an edit the user loses without being asked about it.
        /// </remarks>
        public void MarkDirty(ModelDoc2 model)
        {
            model?.SetSaveFlag();
        }

        private void LoadToolpaths(
            IStorage storage, DocumentReadResult result, ICollection<string> problems)
        {
            foreach (KeyValuePair<string, Operation> waiting in result.ToolpathStreams)
            {
                byte[] bytes = ComStreams.ReadStream(storage, waiting.Key);
                Toolpath path = bytes == null ? null : ToolpathBinary.FromBytes(bytes);

                if (path == null)
                {
                    // A toolpath is always disposable: the parameters that made it are
                    // still here, so this costs a regeneration and nothing else.
                    waiting.Value.Toolpath = null;
                    waiting.Value.State = OperationState.NotGenerated;
                    problems.Add(
                        $"The stored toolpath for '{waiting.Value.Name}' could not be read. " +
                        "Generate it again.");
                    continue;
                }

                waiting.Value.Toolpath = path;
            }
        }

        private static void CopyInto(JobDocument source, JobDocument target)
        {
            foreach (Tool tool in source.Tools)
            {
                target.AddTool(tool);
            }

            Job defaultJob = null;
            foreach (Job job in source.Jobs)
            {
                bool wasDefault = source.IsDefault(job);
                target.Add(job);

                if (wasDefault)
                {
                    defaultJob = job;
                }
            }

            if (defaultJob != null)
            {
                target.MakeDefault(defaultJob);
            }
        }

        private IStorage OpenStorage(ModelDoc2 model, bool storing)
        {
            object node = model.Extension.IGet3rdPartyStorageStore(StorageName, storing);

            // The help is explicit that this comes back as IUnknown and has to be queried
            // for IStorage. The cast is that QueryInterface.
            return node as IStorage;
        }

        /// <summary>
        /// Releases the node, always.
        /// </summary>
        /// <remarks>
        /// Required even when the get returned null, or the third-party node stays locked
        /// and nothing can reach it again this session. The help also says releasing more
        /// often than needed is harmless, which is why this is unconditional rather than
        /// tracked.
        /// </remarks>
        private void ReleaseStorage(ModelDoc2 model, IStorage storage)
        {
            try
            {
                if (storage != null && Marshal.IsComObject(storage))
                {
                    Marshal.ReleaseComObject(storage);
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Releasing the storage object failed.");
            }

            try
            {
                model?.Extension?.IRelease3rdPartyStorageStore(StorageName);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Releasing the third-party storage node failed.");
            }
        }
    }
}

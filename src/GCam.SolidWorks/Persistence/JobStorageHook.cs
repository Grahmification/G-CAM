using System;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using SolidWorks.Interop.sldworks;

namespace GCam.SolidWorks.Persistence
{
    /// <summary>
    /// Saves one document's jobs when SOLIDWORKS says it is safe to.
    /// </summary>
    /// <remarks>
    /// <b>One of these per open part, because the notifications carry no arguments.</b>
    /// <c>SaveToStorageStoreNotify</c> is declared as <c>int Handler()</c> - it says that a
    /// save is happening, not which document is being saved. A single shared handler would
    /// have to guess, and the obvious guess - the active document - is wrong exactly when
    /// it matters, because Save All walks documents that are not in front. Holding the
    /// document in the subscriber removes the question.
    ///
    /// Auto-save is subscribed too. Without it, the recovery copy SOLIDWORKS writes after a
    /// crash would come back with the part's geometry and none of its CAM data, which is
    /// worse than not having a recovery copy - it looks complete.
    ///
    /// <b>A part with no jobs is left alone entirely.</b> See <see cref="OnSaveToStorage"/>.
    /// </remarks>
    public sealed class JobStorageHook : IDisposable
    {
        private readonly PartDoc _part;
        private readonly ModelDoc2 _model;
        private readonly JobDocumentStorage _storage;
        private readonly Func<JobDocument> _jobs;
        private readonly ErrorHandler _errors;
        private readonly IGCamLog _log;

        private bool _subscribed;
        private bool _partHasStoredData;

        /// <param name="partHasStoredData">
        /// Whether this part already carries G-CAM data, which the caller knows because it
        /// has just tried to load it. Once true, every later save writes - including one
        /// that writes an empty document, which is how deleting the last job sticks.
        /// </param>
        public JobStorageHook(
            PartDoc part,
            ModelDoc2 model,
            JobDocumentStorage storage,
            Func<JobDocument> jobs,
            bool partHasStoredData,
            ErrorHandler errors,
            IGCamLog log)
        {
            _part = part ?? throw new ArgumentNullException(nameof(part));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _log = log ?? NullLog.Instance;
            _partHasStoredData = partHasStoredData;

            _part.SaveToStorageStoreNotify += OnSaveToStorage;
            _part.AutoSaveToStorageStoreNotify += OnSaveToStorage;
            _subscribed = true;
        }

        /// <summary>
        /// Entry point. The one moment writing to third-party storage is allowed.
        /// </summary>
        /// <remarks>
        /// <b>A part with no jobs that has never held any is not written to at all.</b>
        /// This fires on every save of every open part while the add-in is loaded, and
        /// <c>IGet3rdPartyStorageStore</c> with <c>storing: true</c> *creates* the storage
        /// node - so without this guard, opening and saving any part at all put a few
        /// hundred bytes of empty G-CAM XML into it, forever, whether or not anyone had
        /// used G-CAM on it.
        ///
        /// <b>Once a part has stored data, every later save writes</b>, including a save of
        /// an empty document. That is not a special case to remove: it is how deleting the
        /// last job actually sticks. Skipping the write there would leave the old jobs in
        /// the file and bring them back on reopen.
        ///
        /// Nothing may escape: an exception thrown back across a SOLIDWORKS notification is
        /// discarded at best and destabilises the host at worst. A failure here loses this
        /// save of the CAM data, which is bad, and is still better than taking the user's
        /// model save down with it.
        /// </remarks>
        private int OnSaveToStorage()
        {
            try
            {
                JobDocument jobs = _jobs();

                if (jobs == null)
                {
                    return 0;
                }

                if (jobs.HasNothingToStore && !_partHasStoredData)
                {
                    _log.Debug(
                        "No jobs, and no G-CAM data in this part already; nothing written.");
                    return 0;
                }

                _storage.Save(_model, jobs);

                // The node exists from here on, so an edit that empties the document later
                // must still be written for the emptying to survive a reopen.
                _partHasStoredData = true;
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(OnSaveToStorage));
            }

            return 0;
        }

        public void Dispose()
        {
            if (!_subscribed)
            {
                return;
            }

            try
            {
                _part.SaveToStorageStoreNotify -= OnSaveToStorage;
                _part.AutoSaveToStorageStoreNotify -= OnSaveToStorage;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Unsubscribing from the storage notifications failed.");
            }
            finally
            {
                _subscribed = false;
            }
        }
    }
}

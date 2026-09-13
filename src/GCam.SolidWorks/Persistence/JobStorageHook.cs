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

        public JobStorageHook(
            PartDoc part,
            ModelDoc2 model,
            JobDocumentStorage storage,
            Func<JobDocument> jobs,
            ErrorHandler errors,
            IGCamLog log)
        {
            _part = part ?? throw new ArgumentNullException(nameof(part));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            _errors = errors ?? throw new ArgumentNullException(nameof(errors));
            _log = log ?? NullLog.Instance;

            _part.SaveToStorageStoreNotify += OnSaveToStorage;
            _part.AutoSaveToStorageStoreNotify += OnSaveToStorage;
            _subscribed = true;
        }

        /// <summary>
        /// Entry point. The one moment writing to third-party storage is allowed.
        /// </summary>
        /// <remarks>
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

                if (jobs != null)
                {
                    _storage.Save(_model, jobs);
                }
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

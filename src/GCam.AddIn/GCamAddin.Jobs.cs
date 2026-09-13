using System;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Model;
using GCam.SolidWorks.PropertyPages;
using SolidWorks.Interop.sldworks;

namespace GCam.AddIn
{
    /// <summary>
    /// Job commands: creating one from the toolbar, and carrying out what the tree asks
    /// for.
    /// </summary>
    /// <remarks>
    /// The add-in implements <see cref="IJobEditor"/> because it is the only place that
    /// holds both halves - the tree lives in GCam.UI, which cannot reach SOLIDWORKS, and
    /// the property pages live in GCam.SolidWorks, which does not know the tree exists.
    ///
    /// These methods are entry points: WPF event handlers call them, so nothing escapes.
    /// </remarks>
    public partial class GCamAddin : IJobEditor
    {
        /// <summary>Entry point 10. Opens a job's property page.</summary>
        public void EditJob(Job job)
        {
            try
            {
                if (job != null)
                {
                    JobPage.Show(job);
                }
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(EditJob));
            }
        }

        /// <summary>Entry point 10. Adds an operation to a job.</summary>
        public void NewOperation(Job job)
        {
            try
            {
                if (job == null)
                {
                    throw new GCamUserException("Create a job before adding an operation.");
                }

                // Opens the shell page. Nothing is added to the job yet - operations
                // have no parameters to set, so creating one would put an empty node in
                // the tree with no way to give it meaning. That arrives with the
                // operation work.
                OperationPage.Show();
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(NewOperation));
            }
        }

        /// <summary>
        /// The New Job toolbar command. The job is built here but only reaches the
        /// document if the page is accepted.
        /// </summary>
        private void CreateJob()
        {
            JobDocument jobs = _jobTreeTabs?.JobsForActiveDocument();

            if (jobs == null)
            {
                // No G-CAM tab for the active document means it is not a part, or there
                // is no document at all.
                throw new GCamUserException("Open a part before creating a job.");
            }

            JobPage.Show(new Job { Name = jobs.NextDefaultName() });
        }

        private JobPropertyPage CreateJobPage()
        {
            // The preview is resolved per show rather than captured, because the page
            // outlives any one document and the stock has to be drawn in whichever part
            // is in front.
            var page = new JobPropertyPage(
                _swApp, _errors, _log, () => _jobTreeTabs?.PreviewForActiveDocument());

            page.Committed += OnJobCommitted;
            return page;
        }

        /// <summary>
        /// Entry point 10. The page was accepted - add the job if it is new, and rebuild
        /// the tree either way.
        /// </summary>
        /// <remarks>
        /// One handler serves both create and edit. A job the document has never seen is
        /// being created; one it already holds is being edited and only needs the tree
        /// refreshing. That is what makes Cancel on a new job leave nothing behind: the
        /// job was never added.
        /// </remarks>
        private void OnJobCommitted(object sender, Job job)
        {
            try
            {
                JobDocument jobs = _jobTreeTabs?.JobsForActiveDocument();
                if (jobs == null || job == null)
                {
                    return;
                }

                if (jobs.FindById(job.Id) == null)
                {
                    jobs.Add(job);
                    _log.Info("Created job '{0}'.", job.Name);
                }
                else
                {
                    _log.Info("Edited job '{0}'.", job.Name);
                }

                _jobTreeTabs.RefreshActiveDocument();

                // The jobs live inside the part, so a CAM change is an unsaved change to
                // it. Without this the user closes the part, is never asked, and the work
                // is gone - SOLIDWORKS only offers us a chance to write during a save it
                // has already decided to do.
                _jobTreeTabs.MarkDirty(_swApp.ActiveDoc as ModelDoc2);

                // Land the selection on the job that was just accepted. The tree is what
                // decides which job the 3D view shows, so without this a job goes
                // straight from having its stock set up to showing nothing - which reads
                // as the preview being broken rather than as nothing being selected.
                _jobTreeTabs.SelectJob(_swApp.ActiveDoc as ModelDoc2, job);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(OnJobCommitted));
            }
        }
    }
}

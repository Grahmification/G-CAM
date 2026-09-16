using System;
using System.Linq;
using GCam.Core.Abstractions;
using GCam.Core.Diagnostics;
using GCam.Core.Generation;
using GCam.Core.Model;
using GCam.Core.Strategies.Contour2d;
using GCam.Core.Tooling;
using GCam.SolidWorks.Selection;
using GCam.SolidWorks.Extraction;
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
        /// <remarks>
        /// Built here and only added to the job if the page is accepted, exactly as
        /// <see cref="CreateJob"/> works - so Cancel leaves nothing behind.
        ///
        /// The new operation is seeded from whatever is selected in the graphics area and
        /// from the first tool in the part, which is a convenience rather than a
        /// requirement: both are editable on the page. HSMWorks pre-fills from the
        /// selection the same way.
        ///
        /// One strategy exists, so no strategy is asked for. When a second lands, this is
        /// where the choice goes - before the page, because
        /// <see cref="Operation.Settings"/> is fixed at construction.
        /// </remarks>
        public void NewOperation(Job job)
        {
            try
            {
                if (job == null)
                {
                    throw new GCamUserException("Create a job before adding an operation.");
                }

                var model = _swApp.ActiveDoc as ModelDoc2;
                JobDocument jobs = _jobTreeTabs?.JobsFor(model);

                if (model == null || jobs == null)
                {
                    throw new GCamUserException("Open a part before adding an operation.");
                }

                var settings = new Contour2dSettings();
                settings.Contours.AddRange(JobSelections.CurrentContourSelections(model));

                var operation = new Operation(settings)
                {
                    Name = job.NextOperationName("2D Contour"),
                };

                Tool tool = jobs.Tools.FirstOrDefault();
                if (tool != null)
                {
                    operation.UseTool(tool);
                }

                _operationJob = job;
                OperationPage.Show(job, operation);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(NewOperation));
            }
        }

        /// <summary>Entry point 10. Opens an existing operation's page.</summary>
        public void EditOperation(Job job, Operation operation)
        {
            try
            {
                if (job == null || operation == null)
                {
                    return;
                }

                _operationJob = job;
                OperationPage.Show(job, operation);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(EditOperation));
            }
        }

        /// <summary>
        /// Entry point 10. The Operation page was accepted.
        /// </summary>
        /// <remarks>
        /// One handler for create and edit, like <see cref="OnJobCommitted"/>: an operation
        /// the job has never seen is being created, one it already holds is being edited.
        /// </remarks>
        private void OnOperationCommitted(object sender, Operation operation)
        {
            try
            {
                var model = _swApp.ActiveDoc as ModelDoc2;

                if (_operationJob == null || operation == null || model == null)
                {
                    return;
                }

                if (!_operationJob.Operations.Contains(operation))
                {
                    _operationJob.Operations.Add(operation);
                    _log.Info("Created operation '{0}'.", operation.Name);
                }
                else
                {
                    // Its inputs changed, so whatever it computed before is out of date.
                    Staleness.OperationEdited(_operationJob, operation);
                    _log.Info("Edited operation '{0}'.", operation.Name);
                }

                _jobTreeTabs.MarkDirty(model);
                _jobTreeTabs.RefreshJobs(model);
                _jobTreeTabs.SelectJob(model, _operationJob);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(OnOperationCommitted));
            }
        }

        /// <summary>
        /// Entry point 11. Computes the toolpaths for a job's operations.
        /// </summary>
        /// <remarks>
        /// <b>Runs on the calling thread, which is SOLIDWORKS' main STA thread.</b> The
        /// architecture calls for generation off it, and <see cref="GenerationQueue"/> is
        /// built for that - but the context factory has to reach SOLIDWORKS for the stock,
        /// the geometry and the coordinate system, and marshalling that back to the STA
        /// thread needs an `SwDispatcher` that does not exist yet. A 2D contour on one
        /// profile is milliseconds; a surface finishing pass will not be, so this has to
        /// move before the strategies get slower.
        ///
        /// Failures land on their operations rather than in a dialog: the queue catches
        /// them, and the tree shows what happened.
        /// </remarks>
        public void GenerateJob(Job job)
        {
            try
            {
                Generate(
                    job,
                    (queue, target) => queue.Generate(target),
                    "job '" + job?.Name + "'");
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(GenerateJob));
            }
        }

        /// <summary>
        /// Entry point 11. Computes the toolpath for one operation.
        /// </summary>
        /// <remarks>
        /// The same queue as a whole job, holding one item - so an operation generated on
        /// its own goes through exactly the path it would as part of a job, including how
        /// a failure is recorded. Everything the remarks on <see cref="GenerateJob"/> say
        /// about the STA thread applies here too.
        /// </remarks>
        public void GenerateOperation(Job job, Operation operation)
        {
            try
            {
                if (operation == null)
                {
                    return;
                }

                Generate(
                    job,
                    (queue, target) => queue.Generate(target, operation),
                    "operation '" + operation.Name + "'");
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(GenerateOperation));
            }
        }

        /// <summary>
        /// Builds the queue for the document in front and runs whichever generate was
        /// asked for, then puts the results on screen.
        /// </summary>
        /// <remarks>
        /// Shared so that one operation and a whole job cannot drift apart in how they
        /// mark the part dirty or refresh the tree - the parts that are easy to forget in
        /// a second copy.
        /// </remarks>
        private void Generate(
            Job job, Func<GenerationQueue, Job, GenerationResult> run, string what)
        {
            var model = _swApp.ActiveDoc as ModelDoc2;
            JobDocument jobs = _jobTreeTabs?.JobsFor(model);

            if (job == null || model == null || jobs == null)
            {
                return;
            }

            var queue = new GenerationQueue(
                _strategies,
                new GenerationContextFactory(_swApp, model, jobs, _log),
                _log);

            GenerationResult result = run(queue, job);

            _log.Info("Generated {0}: {1}", what, result);

            // The toolpaths are part of the document now, so the part has unsaved
            // changes even though nothing about the model moved.
            _jobTreeTabs.MarkDirty(model);

            // Rebuilds the tree, which is also what redraws the toolpaths. The refresh
            // puts the selection back where it was, so generating one operation leaves the
            // user on it rather than jumping to its job.
            _jobTreeTabs.RefreshJobs(model);
        }

        /// <summary>
        /// Entry point 10. The tree changed the model, so the part is dirty.
        /// </summary>
        /// <remarks>
        /// Deleting, duplicating, renaming or suppressing happens in the viewmodel without
        /// a property page, so this is the only thing on those paths that reaches
        /// <c>SetSaveFlag</c>. Without it SOLIDWORKS never offers the save that writes the
        /// storage and the edit is lost at close - see <see cref="IJobEditor"/>.
        /// </remarks>
        public void DocumentChanged()
        {
            try
            {
                _jobTreeTabs?.MarkDirty(_swApp.ActiveDoc as ModelDoc2);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(DocumentChanged));
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

        /// <summary>
        /// The Operation page, built once for the session.
        /// </summary>
        /// <remarks>
        /// The document and its tools are resolved per show rather than captured, for the
        /// same reason the Job page resolves its preview that way: one page serves
        /// whichever part is in front.
        ///
        /// Choosing a tool is passed in as a delegate because the page cannot reach the
        /// browser: it lives in GCam.SolidWorks and the browser is WPF in GCam.UI. See
        /// <see cref="PickToolIntoPart"/>.
        /// </remarks>
        private OperationPropertyPage CreateOperationPage()
        {
            var page = new OperationPropertyPage(
                _swApp,
                _errors,
                _log,
                () => _swApp.ActiveDoc as ModelDoc2,
                () => _jobTreeTabs?.JobsForActiveDocument(),
                PickToolIntoPart);

            page.Committed += OnOperationCommitted;
            return page;
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

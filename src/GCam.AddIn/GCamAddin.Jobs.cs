using System;
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
        /// <b>Creates the operation outright, from whatever is selected in the graphics
        /// area.</b> The Operation property page is still a shell, and until it exists
        /// this is the only way an operation can come into being - which otherwise makes
        /// the whole toolpath pipeline untestable, since nothing else can produce one to
        /// generate.
        ///
        /// The creation itself is not throwaway: with a page, New Operation still creates
        /// the operation and the page then edits it. What the page replaces is the
        /// guessing below - seeding from the current selection and from the first tool in
        /// the part. Pre-filling from the selection is what HSMWorks does anyway.
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

                if (settings.Contours.Count == 0)
                {
                    throw new GCamUserException(
                        "Select the edges or faces of a closed profile, then add the " +
                        "operation. Choosing geometry on the page comes with the " +
                        "Operation page.");
                }

                var operation = new Operation(settings)
                {
                    Name = job.NextOperationName("2D Contour"),
                };

                operation.UseTool(DefaultTool(jobs));

                job.Operations.Add(operation);

                _log.Info(
                    "Created operation '{0}' from {1} selected entities.",
                    operation.Name, settings.Contours.Count);

                _jobTreeTabs.MarkDirty(model);
                _jobTreeTabs.RefreshJobs(model);
                _jobTreeTabs.SelectJob(model, job);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(NewOperation));
            }
        }

        /// <summary>
        /// The tool a new operation starts with.
        /// </summary>
        /// <remarks>
        /// The first tool already in the part, or a plainly-named stand-in when it has
        /// none. The stand-in is a stopgap for the same reason as the rest of this path:
        /// choosing a tool is the property page's job, and until that exists an operation
        /// with no tool cannot be generated at all. It is named and logged so nobody
        /// mistakes it for a considered choice.
        /// </remarks>
        private Tool DefaultTool(JobDocument jobs)
        {
            if (jobs.Tools.Count > 0)
            {
                return jobs.Tools[0];
            }

            var tool = new Tool
            {
                Number = 1,
                Name = "Stand-in 6mm end mill",
                Type = ToolType.FlatEndMill,
                Geometry = { Diameter = 6, FluteLength = 20, ShoulderLength = 20, FluteCount = 3 },
                Cutting = { SpindleRpm = 8000, CuttingFeed = 800, PlungeFeed = 300 },
            };

            jobs.AddTool(tool);

            _log.Warn(
                "This part had no tools, so a stand-in {0} was added. Choose a real one " +
                "once the Operation page can.", tool.DisplayName);

            return tool;
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

                GenerationResult result = queue.Generate(job);

                _log.Info("Generated job '{0}': {1}", job.Name, result);

                // The toolpaths are part of the document now, so the part has unsaved
                // changes even though nothing about the model moved.
                _jobTreeTabs.MarkDirty(model);

                _jobTreeTabs.RefreshJobs(model);
                _jobTreeTabs.SelectJob(model, job);
            }
            catch (Exception ex)
            {
                _errors.Handle(ex, nameof(GenerateJob));
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

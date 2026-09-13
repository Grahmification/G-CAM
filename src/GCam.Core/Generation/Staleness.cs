using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Model;

namespace GCam.Core.Generation
{
    /// <summary>
    /// What a change invalidates.
    /// </summary>
    /// <remarks>
    /// **Deliberately over-eager.** These rules will sometimes mark an operation stale when
    /// the toolpath would have come out identical - a rebuild that moved a face the
    /// operation does not touch, say. The opposite error posts a path that no longer
    /// matches the model, and that scraps parts. The asymmetry is the point, not a
    /// shortcoming to be tuned away later.
    ///
    /// Staleness never destroys anything: the toolpath stays, drawn faded, and posting a
    /// stale operation warns. The cost of a false positive is a regeneration.
    /// </remarks>
    public static class Staleness
    {
        /// <summary>
        /// Marks one operation's toolpath as no longer trustworthy.
        /// </summary>
        /// <remarks>
        /// Only an operation that actually has a result can go stale. One that never
        /// generated, or failed, or is being generated right now, is left alone - "stale"
        /// would be a downgrade from "failed" and a lie about the other two.
        /// </remarks>
        public static bool MarkStale(Operation operation)
        {
            if (operation == null)
            {
                return false;
            }

            if (operation.State != OperationState.Generated
                && operation.State != OperationState.Warning)
            {
                return false;
            }

            operation.State = OperationState.Stale;
            return true;
        }

        /// <summary>
        /// The operations below this one that machine what it leaves behind.
        /// </summary>
        /// <remarks>
        /// Position is the dependency: operations run in tree order, so an operation that
        /// rest-machines depends on everything above it. Only the ones that say they do -
        /// <see cref="Strategies.StrategySettings.DependsOnPrecedingStock"/> - are
        /// affected; a contour cutting a fixed profile does not care what came before.
        /// </remarks>
        public static IReadOnlyList<Operation> DependentsBelow(Job job, Operation operation)
        {
            if (job?.Operations == null || operation == null)
            {
                return new Operation[0];
            }

            int index = job.Operations.IndexOf(operation);
            if (index < 0)
            {
                return new Operation[0];
            }

            return job.Operations
                .Skip(index + 1)
                .Where(o => o?.Settings != null && o.Settings.DependsOnPrecedingStock)
                .ToList();
        }

        /// <summary>
        /// An operation's own parameters, geometry, tool or heights changed.
        /// </summary>
        /// <remarks>
        /// Dependents below are only affected when the edited operation is enabled. A
        /// disabled operation removes no material, so changing it changes nothing for
        /// whatever machines afterwards.
        /// </remarks>
        public static void OperationEdited(Job job, Operation operation)
        {
            MarkStale(operation);

            if (operation != null && operation.Enabled)
            {
                MarkAll(DependentsBelow(job, operation));
            }
        }

        /// <summary>
        /// An operation was enabled or disabled.
        /// </summary>
        /// <remarks>
        /// Its own toolpath is still perfectly good - nothing about the operation changed -
        /// but what reaches the operations below it just did, in both directions.
        /// </remarks>
        public static void OperationEnabledChanged(Job job, Operation operation)
        {
            MarkAll(DependentsBelow(job, operation));
        }

        /// <summary>
        /// Operations were reordered, or one was removed.
        /// </summary>
        /// <remarks>
        /// Every dependent in the job, rather than working out which side of the change
        /// each one fell on. Reordering is rare and a spare regeneration is cheap;
        /// index arithmetic that is subtly wrong is not.
        /// </remarks>
        public static void OperationOrderChanged(Job job)
        {
            if (job?.Operations == null)
            {
                return;
            }

            MarkAll(job.Operations.Where(o => o?.Settings?.DependsOnPrecedingStock == true));
        }

        /// <summary>
        /// The job's stock, coordinate system, work offset or body selection changed.
        /// </summary>
        public static void JobChanged(Job job)
        {
            MarkAll(job?.Operations);
        }

        /// <summary>
        /// A tool in the part changed shape.
        /// </summary>
        /// <remarks>
        /// Every operation cutting with it, across every job. This is the reach that makes
        /// a shared tool list worth having and worth warning about: editing one cutter's
        /// geometry invalidates everything that uses it, which is correct, because the
        /// cutter really did change.
        /// </remarks>
        public static void ToolChanged(JobDocument document, string toolId)
        {
            if (document == null || string.IsNullOrEmpty(toolId))
            {
                return;
            }

            MarkAll(ToolUsage.UsesOf(document, toolId).Select(u => u.Operation));
        }

        /// <summary>
        /// SOLIDWORKS rebuilt the part.
        /// </summary>
        /// <remarks>
        /// Everything, everywhere. A rebuild can move any face, and nothing short of
        /// re-resolving every selection would say which - which costs as much as
        /// regenerating. The bluntest rule here, and the one most likely to mark something
        /// stale unnecessarily.
        /// </remarks>
        public static void ModelRebuilt(JobDocument document)
        {
            if (document?.Jobs == null)
            {
                return;
            }

            foreach (Job job in document.Jobs)
            {
                MarkAll(job?.Operations);
            }
        }

        private static void MarkAll(IEnumerable<Operation> operations)
        {
            foreach (Operation operation in operations ?? Enumerable.Empty<Operation>())
            {
                MarkStale(operation);
            }
        }
    }
}

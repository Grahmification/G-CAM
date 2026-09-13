using GCam.Core.Model;
using GCam.Core.Strategies;

namespace GCam.Core.Generation
{
    /// <summary>
    /// Resolves everything a strategy needs for one operation.
    /// </summary>
    /// <remarks>
    /// Declared in Core and implemented in GCam.SolidWorks, because building a context
    /// means asking the model where its stock and faces are - the one part of generation
    /// that needs COM. The queue holds this interface and never knows that.
    ///
    /// It runs on whichever thread the queue is on, so an implementation that touches
    /// SOLIDWORKS marshals to the main STA thread itself, through `SwDispatcher`.
    /// </remarks>
    public interface IGenerationContextFactory
    {
        /// <summary>
        /// Builds the context for one operation.
        /// </summary>
        /// <exception cref="Diagnostics.GCamUserException">
        /// Something the user has to fix: no tool chosen, a height measured from a face
        /// that has gone, stock that cannot be computed. The queue turns this into a
        /// failed operation carrying the message, rather than stopping the run.
        /// </exception>
        GenerationContext Create(Job job, Operation operation);
    }
}

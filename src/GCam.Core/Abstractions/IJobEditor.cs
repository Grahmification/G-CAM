using GCam.Core.Model;

namespace GCam.Core.Abstractions
{
    /// <summary>
    /// What the job tree asks for when the user wants to edit something.
    /// </summary>
    /// <remarks>
    /// The tree is WPF in GCam.UI, which references Core and never SOLIDWORKS, but
    /// editing a job means opening a SOLIDWORKS PropertyManager page. So the tree states
    /// intent through this interface and GCam.AddIn - which owns the pages - carries it
    /// out. Core declares, the outer layers implement; the same arrangement as
    /// IGCamLog and IErrorPresenter.
    ///
    /// Implementations are called from WPF event handlers, so they are entry points in
    /// the sense of docs/error-handling.md and must not let exceptions escape.
    /// </remarks>
    public interface IJobEditor
    {
        /// <summary>Opens the job's property page.</summary>
        void EditJob(Job job);

        /// <summary>Adds an operation to the given job, or to the default job when null.</summary>
        void NewOperation(Job job);

        /// <summary>
        /// Computes the toolpaths for a job's operations.
        /// </summary>
        /// <remarks>
        /// Stated here rather than done in the tree because generation needs the model -
        /// the stock, the selected geometry, the coordinate system - and the tree cannot
        /// see SOLIDWORKS. The same arrangement as the rest of this interface.
        /// </remarks>
        void GenerateJob(Job job);
    }
}

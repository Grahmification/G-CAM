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

        /// <summary>Opens an existing operation's property page.</summary>
        void EditOperation(Job job, Operation operation);

        /// <summary>
        /// Computes the toolpaths for a job's operations.
        /// </summary>
        /// <remarks>
        /// Stated here rather than done in the tree because generation needs the model -
        /// the stock, the selected geometry, the coordinate system - and the tree cannot
        /// see SOLIDWORKS. The same arrangement as the rest of this interface.
        /// </remarks>
        void GenerateJob(Job job);

        /// <summary>Computes the toolpath for one operation.</summary>
        /// <remarks>
        /// Separate from <see cref="GenerateJob"/> rather than a flag on it, because the
        /// two are different requests: a job generate runs everything in tree order and is
        /// what you ask for before posting, while this is what you ask for while setting
        /// one operation up. <see cref="Generation.GenerationQueue"/> already offers both.
        /// </remarks>
        void GenerateOperation(Job job, Operation operation);

        /// <summary>
        /// The tree changed the model itself, so the part has unsaved CAM changes.
        /// </summary>
        /// <remarks>
        /// Deleting, duplicating, renaming, suppressing or re-defaulting happens in the
        /// viewmodel against Core objects, with no property page involved - so nothing on
        /// those paths would otherwise reach <c>IModelDoc2::SetSaveFlag</c>. Without it
        /// SOLIDWORKS never offers the save that writes the document's storage, and the
        /// edit is gone at close with nobody asked: the same failure the property pages
        /// already guard against by marking dirty on commit.
        ///
        /// Stated rather than done here for the usual reason - the tree cannot see
        /// SOLIDWORKS - and deliberately carries no argument. The tree the user is
        /// clicking belongs to the document in front, which is what the implementation
        /// resolves.
        /// </remarks>
        void DocumentChanged();
    }
}

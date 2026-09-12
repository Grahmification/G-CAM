using System;

namespace GCam.Core.Diagnostics
{
    /// <summary>
    /// Shows problems to the user. Implemented in GCam.UI; Core only knows the contract.
    /// </summary>
    /// <remarks>
    /// Implementations must be safe to call from any thread and must never throw -
    /// they are invoked from inside exception handling, where a second failure would
    /// lose the original.
    /// </remarks>
    public interface IErrorPresenter
    {
        /// <summary>
        /// Reports a defect: full detail, stack trace available to copy.
        /// </summary>
        /// <param name="context">Entry point the failure came from, e.g. "OnCommand".</param>
        void ShowError(Exception ex, string context);

        /// <summary>
        /// Reports an expected problem in the user's own terms. No stack, no defect
        /// framing - "Tool Ø12 will not fit a Ø8 pocket".
        /// </summary>
        void ShowUserError(string message);
    }
}

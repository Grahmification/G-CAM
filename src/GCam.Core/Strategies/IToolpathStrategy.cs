using System;
using System.Threading;
using GCam.Core.Model;

namespace GCam.Core.Strategies
{
    /// <summary>
    /// Turns one operation into a toolpath.
    /// </summary>
    /// <remarks>
    /// **Strategies are pure**: the same context in gives the same toolpath out, with no
    /// COM, no file access and no shared state. That is what lets them run on a worker
    /// thread, be tested without SOLIDWORKS, and be re-run to check a result.
    ///
    /// Implementations report progress from 0 to 1 and check the cancellation token often
    /// enough that cancelling feels immediate - between passes at worst, not at the end.
    ///
    /// Throwing is the way to fail. A <see cref="Diagnostics.GCamUserException"/> carries a
    /// message the user is meant to read - "the tool is too big for this pocket" - and the
    /// queue puts it on the operation. Anything else is a bug, and gets logged as one.
    /// </remarks>
    public interface IToolpathStrategy
    {
        /// <summary>Which strategy this implements. Must match the settings it reads.</summary>
        StrategyId Id { get; }

        /// <summary>
        /// Computes the toolpath. Never returns null; an operation with nothing to cut
        /// returns an empty path.
        /// </summary>
        Toolpath Generate(
            GenerationContext context,
            IProgress<double> progress,
            CancellationToken cancellation);
    }
}

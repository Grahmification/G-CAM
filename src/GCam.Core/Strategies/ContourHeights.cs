using System.Collections.Generic;
using GCam.Core.Model.Heights;

namespace GCam.Core.Strategies
{
    /// <summary>
    /// Resolves an operation's heights once per contour, for when the cutting heights are
    /// measured from the contour being cut.
    /// </summary>
    /// <remarks>
    /// **One bad chain does not condemn the operation.** Heights that are in order for one
    /// contour can be out of order for another - a chain above the stock top lifts its top
    /// above the feed height - so each contour is validated against its own numbers. A
    /// contour whose heights fail is reported and left uncut, and the rest are cut, which
    /// is how a contour consumed by a negative tangential extension is already treated.
    /// Only when every contour fails is there nothing to generate.
    ///
    /// In Core rather than beside the extraction that calls it, because it is a rule with
    /// a right answer and a test can reach it here.
    /// </remarks>
    public static class ContourHeights
    {
        /// <summary>
        /// The contours whose heights resolve and are in order, each carrying them.
        /// </summary>
        /// <param name="warnings">
        /// Receives a line for every contour left out - but only when some survive. When
        /// none do, the caller has a failure to report instead, and a warning per contour
        /// saying the same thing would bury it.
        /// </param>
        /// <param name="failure">
        /// The first problem found, for the caller to report when nothing survives. Null
        /// when every contour resolved.
        /// </param>
        public static IReadOnlyList<ResolvedContour> Resolve(
            OperationHeights heights,
            HeightContext context,
            IReadOnlyList<ResolvedContour> contours,
            ICollection<string> warnings,
            out string failure)
        {
            failure = null;
            var kept = new List<ResolvedContour>();

            if (contours == null || contours.Count == 0)
            {
                // Still worth asking: a strategy with no contours and a height measured
                // from one gets told so, rather than getting nothing and no reason.
                IReadOnlyList<string> problems = heights.Validate(context);
                failure = problems.Count > 0 ? problems[0] : null;
                return kept;
            }

            var skipped = new List<string>();

            foreach (ResolvedContour contour in contours)
            {
                HeightContext own = context.ForContour(contour.Level);
                IReadOnlyList<string> problems = heights.Validate(own);

                if (problems.Count > 0)
                {
                    failure = failure ?? problems[0];
                    skipped.Add(
                        $"The contour at Z {contour.Level:0.###}mm has not been cut: {problems[0]}");
                    continue;
                }

                heights.TryResolve(own, out ResolvedHeights resolved);
                kept.Add(contour.WithHeights(resolved));
            }

            if (kept.Count > 0)
            {
                foreach (string line in skipped)
                {
                    warnings?.Add(line);
                }
            }

            return kept;
        }
    }
}

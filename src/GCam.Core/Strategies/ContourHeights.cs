using System;
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
    /// **One retract plane for every contour.** When the feed height follows the contour
    /// and some contour's feed is above the retract as entered, the retract is lifted to
    /// the highest such feed for all of them, not just for that one - more air-cutting on
    /// the lower contours, but the tool always goes back to the same place.
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
            var candidates = new List<ResolvedContour>();
            double? floor = null;

            // First the contours that can be cut at all, and the highest feed height among
            // them - which is where the one retract plane has to be when any feed is above
            // the retract as entered. A contour left out does not get a say in it.
            foreach (ResolvedContour contour in contours)
            {
                if (Usable(heights, context.ForContour(contour.Level), contour, skipped, ref failure))
                {
                    candidates.Add(contour);

                    if (heights.TryResolve(HeightKind.Feed, context.ForContour(contour.Level), out double feed))
                    {
                        floor = floor.HasValue ? Math.Max(floor.Value, feed) : feed;
                    }
                }
            }

            HeightContext lifted = floor.HasValue ? context.WithRetractFloor(floor.Value) : context;
            string correction = null;

            // Then again at the shared retract. Lifting it can put it above a clearance
            // that does not follow it, which nothing corrects, so each contour is checked
            // a second time rather than assumed still usable.
            foreach (ResolvedContour contour in candidates)
            {
                HeightContext own = lifted.ForContour(contour.Level);

                if (!Usable(heights, own, contour, skipped, ref failure))
                {
                    continue;
                }

                heights.TryResolve(own, out ResolvedHeights resolved);
                kept.Add(contour.WithHeights(resolved));

                // One plane, so one lift: every contour says the same thing, once is enough.
                correction = correction ?? OperationHeights.DescribeCorrections(resolved);
            }

            if (kept.Count > 0)
            {
                foreach (string line in skipped)
                {
                    warnings?.Add(line);
                }

                if (correction != null)
                {
                    warnings?.Add(correction);
                }
            }

            return kept;
        }

        /// <summary>
        /// True when the heights are usable for this contour; otherwise records why not.
        /// </summary>
        private static bool Usable(
            OperationHeights heights,
            HeightContext own,
            ResolvedContour contour,
            ICollection<string> skipped,
            ref string failure)
        {
            IReadOnlyList<string> problems = heights.Validate(own);

            if (problems.Count == 0)
            {
                return true;
            }

            failure = failure ?? problems[0];
            skipped.Add($"The contour at Z {contour.Level:0.###}mm has not been cut: {problems[0]}");
            return false;
        }
    }
}

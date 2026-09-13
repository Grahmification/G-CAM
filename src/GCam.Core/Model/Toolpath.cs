using System;
using System.Collections.Generic;
using System.Linq;
using GCam.Core.Geometry.Primitives;

namespace GCam.Core.Model
{
    /// <summary>
    /// What a strategy produces: an ordered run of moves for one operation.
    /// </summary>
    /// <remarks>
    /// **The first move says where the tool starts.** Nothing is drawn or cut on the way
    /// to it, because there is no previous position to come from - a toolpath describes
    /// where the tool goes, and the machine is wherever the last operation left it.
    ///
    /// Millimetres, in the operation's frame. Conversion to metres stays at the SOLIDWORKS
    /// edge, and a rotated frame has already been applied by whoever built this.
    ///
    /// This is the artifact that gets persisted and drawn. `CLData` is derived from it
    /// when posting, which keeps the rule that posts consume CLData and never a Toolpath -
    /// see docs/architecture.md.
    /// </remarks>
    public sealed class Toolpath
    {
        private readonly List<Move> _moves = new List<Move>();

        public Toolpath()
        {
        }

        public Toolpath(IEnumerable<Move> moves)
        {
            if (moves != null)
            {
                _moves.AddRange(moves.Where(m => m != null));
            }
        }

        public IReadOnlyList<Move> Moves => _moves;

        /// <summary>
        /// True when there is nothing to draw or post. One move is still empty: it says
        /// where the tool is and goes nowhere.
        /// </summary>
        public bool IsEmpty => _moves.Count < 2;

        /// <summary>Where the tool has to be before this path makes sense.</summary>
        public Vec3? Start => _moves.Count > 0 ? _moves[0].End : (Vec3?)null;

        public Toolpath Add(Move move)
        {
            if (move == null)
            {
                throw new ArgumentNullException(nameof(move));
            }

            _moves.Add(move);
            return this;
        }

        public Toolpath AddRange(IEnumerable<Move> moves)
        {
            foreach (Move move in moves ?? Enumerable.Empty<Move>())
            {
                Add(move);
            }

            return this;
        }

        /// <summary>
        /// The box the path occupies, ignoring the bulge of any arcs.
        /// </summary>
        /// <remarks>
        /// Corner points only, so an arc bulging outside its endpoints is not counted.
        /// Good enough for framing a view; not good enough to decide whether a path stays
        /// inside the stock, which is a job for the simulator.
        /// </remarks>
        public Bounds? Extent
        {
            get
            {
                if (_moves.Count == 0)
                {
                    return null;
                }

                Vec3 min = _moves[0].End;
                Vec3 max = min;

                foreach (Move move in _moves)
                {
                    min = new Vec3(
                        Math.Min(min.X, move.End.X),
                        Math.Min(min.Y, move.End.Y),
                        Math.Min(min.Z, move.End.Z));
                    max = new Vec3(
                        Math.Max(max.X, move.End.X),
                        Math.Max(max.Y, move.End.Y),
                        Math.Max(max.Z, move.End.Z));
                }

                return new Bounds(min, max);
            }
        }

        /// <summary>
        /// A copy that can be handed out without anyone being able to change this one.
        /// </summary>
        /// <remarks>
        /// The moves themselves are shared rather than duplicated, and that is a genuine
        /// deep copy because <see cref="Move"/> and <see cref="ArcData"/> are immutable.
        /// Copying fifty thousand of them to achieve nothing would be the alternative.
        /// </remarks>
        public Toolpath Clone() => new Toolpath(_moves);

        public override string ToString() => $"Toolpath ({_moves.Count} moves)";
    }
}

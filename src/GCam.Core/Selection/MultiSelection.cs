using System;
using System.Collections.Generic;
using System.Linq;

namespace GCam.Core.Selection
{
    /// <summary>
    /// What is selected in a list or a tree, when more than one thing can be.
    /// </summary>
    /// <remarks>
    /// The rules behind click, Ctrl-click and Shift-click, with nothing in them that knows
    /// what is being selected. They live here rather than in the viewmodel that uses them
    /// for the reason <c>ToolSearch</c> does: there is no <c>GCam.UI.Tests</c>, and a range
    /// that runs the wrong way or an anchor left pointing at something no longer selected
    /// are exactly the mistakes a headless test catches and a click-through does not.
    ///
    /// <b>The anchor is two things at once, on purpose.</b> It is where Shift-click
    /// measures a range from, and it is what a command that can only act on one thing acts
    /// on - Edit, Rename, Make Default. Keeping them as one field is what makes
    /// "select a row, then Shift-click three below it, then Edit" behave the way it reads:
    /// the range grows from the row that was clicked first, and that row is still the one
    /// being edited.
    ///
    /// Every mutator answers whether it changed anything, so a caller can skip the work
    /// that follows a selection change - repainting a 3D view, most of all - when a click
    /// landed on what was already selected.
    ///
    /// Items are compared with <see cref="EqualityComparer{T}.Default"/>, which for the
    /// tree nodes this was written for is reference equality.
    /// </remarks>
    public sealed class MultiSelection<T>
        where T : class
    {
        private static readonly EqualityComparer<T> Same = EqualityComparer<T>.Default;

        private readonly List<T> _items = new List<T>();

        /// <summary>What is selected, in the order it was selected.</summary>
        public IReadOnlyList<T> Items => _items;

        /// <summary>
        /// Where a range is measured from, and what a single-item command acts on. Null
        /// when nothing is selected.
        /// </summary>
        public T Anchor { get; private set; }

        public int Count => _items.Count;

        public bool IsEmpty => _items.Count == 0;

        public bool Contains(T item) => item != null && _items.Contains(item, Same);

        /// <summary>Selects one item, dropping everything else. A plain click.</summary>
        public bool Select(T item)
        {
            if (item == null)
            {
                return Clear();
            }

            bool changed = _items.Count != 1 || !Same.Equals(_items[0], item);

            _items.Clear();
            _items.Add(item);

            changed |= !Same.Equals(Anchor, item);
            Anchor = item;

            return changed;
        }

        /// <summary>
        /// Selects exactly these items, in the order given, anchored on the first of them
        /// that is present.
        /// </summary>
        /// <remarks>
        /// For putting a selection back after the tree has been rebuilt from the model.
        /// The caller says which anchor it wants because the node it was on may be gone,
        /// and falling back to the first item is better than to nothing.
        /// </remarks>
        public bool SelectAll(IEnumerable<T> items, T anchor = null)
        {
            List<T> wanted = Distinct(items);

            bool changed = !_items.SequenceEqual(wanted, Same);

            _items.Clear();
            _items.AddRange(wanted);

            T settled = anchor != null && Contains(anchor) ? anchor : _items.FirstOrDefault();

            changed |= !Same.Equals(Anchor, settled);
            Anchor = settled;

            return changed;
        }

        /// <summary>
        /// Adds an item to the selection, or takes it out again. A Ctrl-click.
        /// </summary>
        /// <remarks>
        /// Ctrl-clicking a selected item deselects it, which leaves the anchor pointing at
        /// something the user can see is not selected. It moves to whatever is still
        /// selected instead, so the next Shift-click and the next Edit both act on
        /// something real.
        /// </remarks>
        public bool Toggle(T item)
        {
            if (item == null)
            {
                return false;
            }

            if (Contains(item))
            {
                _items.RemoveAll(existing => Same.Equals(existing, item));

                if (Same.Equals(Anchor, item))
                {
                    Anchor = _items.LastOrDefault();
                }

                return true;
            }

            _items.Add(item);
            Anchor = item;

            return true;
        }

        /// <summary>
        /// Selects everything between the anchor and <paramref name="item"/>. A
        /// Shift-click.
        /// </summary>
        /// <remarks>
        /// <paramref name="order"/> is the order the user sees - for a tree, its rows from
        /// the top down with collapsed children left out, which is what makes a range mean
        /// what it looks like it means. The anchor stays where it is, so Shift-clicking
        /// again re-measures from the same row rather than growing whatever was selected
        /// last.
        ///
        /// Falls back to selecting the one item when there is no anchor, or when either end
        /// is not in <paramref name="order"/> - a range with one end missing has no
        /// meaning, and doing nothing would look like a dead click.
        /// </remarks>
        public bool ExtendTo(T item, IReadOnlyList<T> order)
        {
            if (item == null)
            {
                return false;
            }

            if (Anchor == null || order == null)
            {
                return Select(item);
            }

            int from = IndexOf(order, Anchor);
            int to = IndexOf(order, item);

            if (from < 0 || to < 0)
            {
                return Select(item);
            }

            int first = Math.Min(from, to);
            int last = Math.Max(from, to);

            var range = new List<T>();

            for (int i = first; i <= last; i++)
            {
                range.Add(order[i]);
            }

            // The anchor is asked for rather than put back afterwards. Left to itself
            // SelectAll settles on the first row of the range, which is the anchor only
            // when the range runs downwards - and it would then report the anchor as having
            // moved, on every upward Shift-click, when nothing had changed at all. The
            // caller redraws a 3D view and writes back into a WPF TreeView on that answer.
            return SelectAll(range, Anchor);
        }

        public bool Clear()
        {
            if (_items.Count == 0 && Anchor == null)
            {
                return false;
            }

            _items.Clear();
            Anchor = null;

            return true;
        }

        private static int IndexOf(IReadOnlyList<T> order, T item)
        {
            for (int i = 0; i < order.Count; i++)
            {
                if (Same.Equals(order[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        private static List<T> Distinct(IEnumerable<T> items)
        {
            var result = new List<T>();

            foreach (T item in items ?? Enumerable.Empty<T>())
            {
                if (item != null && !result.Contains(item, Same))
                {
                    result.Add(item);
                }
            }

            return result;
        }
    }
}

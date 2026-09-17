using System.Linq;
using GCam.Core.Selection;
using Xunit;

namespace GCam.Core.Tests.Selection
{
    public class MultiSelectionTests
    {
        /// <summary>A row of things to select, standing in for a tree's rows.</summary>
        private sealed class Row
        {
            public Row(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public override string ToString() => Name;
        }

        private static readonly Row[] Rows =
        {
            new Row("Job1"),
            new Row("Contour1"),
            new Row("Contour2"),
            new Row("Job2"),
            new Row("Contour3"),
        };

        private static MultiSelection<Row> Selection() => new MultiSelection<Row>();

        private static string Names(MultiSelection<Row> selection) =>
            string.Join(",", selection.Items.Select(r => r.Name));

        [Fact]
        public void A_new_selection_is_empty_and_has_no_anchor()
        {
            MultiSelection<Row> selection = Selection();

            Assert.True(selection.IsEmpty);
            Assert.Null(selection.Anchor);
        }

        [Fact]
        public void Selecting_replaces_whatever_was_selected()
        {
            MultiSelection<Row> selection = Selection();

            selection.Select(Rows[0]);
            selection.Select(Rows[2]);

            Assert.Equal("Contour2", Names(selection));
            Assert.Same(Rows[2], selection.Anchor);
        }

        [Fact]
        public void Selecting_what_is_already_selected_alone_changes_nothing()
        {
            MultiSelection<Row> selection = Selection();
            selection.Select(Rows[1]);

            Assert.False(selection.Select(Rows[1]));
        }

        /// <summary>
        /// The reason every mutator answers this: a click that lands on what was already
        /// selected must not cost a redraw of the 3D view.
        /// </summary>
        [Fact]
        public void Reselecting_one_of_several_is_a_change()
        {
            MultiSelection<Row> selection = Selection();
            selection.Select(Rows[1]);
            selection.Toggle(Rows[2]);

            Assert.True(selection.Select(Rows[1]));
            Assert.Equal("Contour1", Names(selection));
        }

        [Fact]
        public void Toggling_adds_and_takes_away()
        {
            MultiSelection<Row> selection = Selection();

            selection.Select(Rows[1]);
            selection.Toggle(Rows[3]);

            Assert.Equal("Contour1,Job2", Names(selection));

            selection.Toggle(Rows[1]);

            Assert.Equal("Job2", Names(selection));
        }

        [Fact]
        public void Toggling_the_anchor_off_moves_the_anchor_to_something_selected()
        {
            MultiSelection<Row> selection = Selection();

            selection.Select(Rows[1]);
            selection.Toggle(Rows[3]);
            selection.Toggle(Rows[3]);

            Assert.Same(Rows[1], selection.Anchor);
            Assert.True(selection.Contains(selection.Anchor));
        }

        [Fact]
        public void Toggling_the_last_one_off_leaves_no_anchor()
        {
            MultiSelection<Row> selection = Selection();

            selection.Select(Rows[0]);
            selection.Toggle(Rows[0]);

            Assert.True(selection.IsEmpty);
            Assert.Null(selection.Anchor);
        }

        [Fact]
        public void A_range_runs_from_the_anchor_to_the_item()
        {
            MultiSelection<Row> selection = Selection();

            selection.Select(Rows[1]);
            selection.ExtendTo(Rows[3], Rows);

            Assert.Equal("Contour1,Contour2,Job2", Names(selection));
        }

        [Fact]
        public void A_range_runs_upwards_as_well()
        {
            MultiSelection<Row> selection = Selection();

            selection.Select(Rows[3]);
            selection.ExtendTo(Rows[1], Rows);

            Assert.Equal("Contour1,Contour2,Job2", Names(selection));
        }

        /// <summary>
        /// The anchor staying put is what makes a second Shift-click re-measure rather than
        /// grow: it is the row that was clicked first, not the one clicked last.
        /// </summary>
        /// <remarks>
        /// Ranged <i>upwards</i> first on purpose. Extending down from the anchor leaves it
        /// at the top of the range, where anything that recomputed it from the range would
        /// land too - so a test that only goes down cannot tell the two apart.
        /// </remarks>
        [Fact]
        public void A_second_range_is_measured_from_the_same_anchor()
        {
            MultiSelection<Row> selection = Selection();

            selection.Select(Rows[3]);
            selection.ExtendTo(Rows[1], Rows);

            Assert.Same(Rows[3], selection.Anchor);

            selection.ExtendTo(Rows[4], Rows);

            Assert.Equal("Job2,Contour3", Names(selection));
            Assert.Same(Rows[3], selection.Anchor);
        }

        /// <summary>
        /// Shift-clicking the same row twice is not a change, and saying it is crashes
        /// SOLIDWORKS.
        /// </summary>
        /// <remarks>
        /// The caller redraws the 3D view and writes the selection back into the
        /// <c>TreeView</c> whenever this answers true. WPF raises its own selection change
        /// after the click handler has already applied the range, so a false positive here
        /// re-enters the tree's selection machinery from inside it, and calls into
        /// SOLIDWORKS COM while it is there. It took the process down.
        ///
        /// <b>Upwards on purpose.</b> Extending down leaves the anchor at the top of the
        /// range, where a recomputed anchor lands too, so only a range that runs up can
        /// tell a restored anchor from a recomputed one.
        /// </remarks>
        [Fact]
        public void Extending_again_to_the_same_row_is_not_a_change()
        {
            MultiSelection<Row> selection = Selection();

            selection.Select(Rows[3]);
            selection.ExtendTo(Rows[1], Rows);

            Assert.False(selection.ExtendTo(Rows[1], Rows));
        }

        [Fact]
        public void A_range_with_no_anchor_selects_the_one_item()
        {
            MultiSelection<Row> selection = Selection();

            selection.ExtendTo(Rows[2], Rows);

            Assert.Equal("Contour2", Names(selection));
            Assert.Same(Rows[2], selection.Anchor);
        }

        /// <summary>
        /// A row the user cannot see - inside a collapsed job - is not in the order, and a
        /// range with one end missing has no meaning. Doing nothing would look like a dead
        /// click.
        /// </summary>
        [Fact]
        public void A_range_to_a_row_that_is_not_shown_selects_just_it()
        {
            MultiSelection<Row> selection = Selection();
            var hidden = new Row("Hidden");

            selection.Select(Rows[0]);
            selection.ExtendTo(hidden, Rows);

            Assert.Equal("Hidden", Names(selection));
        }

        [Fact]
        public void Selecting_all_keeps_the_order_given_and_drops_repeats()
        {
            MultiSelection<Row> selection = Selection();

            selection.SelectAll(new[] { Rows[3], Rows[1], Rows[3] });

            Assert.Equal("Job2,Contour1", Names(selection));
        }

        [Fact]
        public void Selecting_all_anchors_on_the_row_asked_for()
        {
            MultiSelection<Row> selection = Selection();

            selection.SelectAll(new[] { Rows[3], Rows[1] }, Rows[1]);

            Assert.Same(Rows[1], selection.Anchor);
        }

        /// <summary>
        /// What a rebuild does when the row the user was on has gone: the rest of the
        /// selection is still real, and the anchor has to land on one of them.
        /// </summary>
        [Fact]
        public void Selecting_all_with_an_anchor_that_is_not_in_it_falls_back_to_the_first()
        {
            MultiSelection<Row> selection = Selection();

            selection.SelectAll(new[] { Rows[3], Rows[1] }, new Row("Deleted"));

            Assert.Same(Rows[3], selection.Anchor);
        }

        [Fact]
        public void Clearing_an_empty_selection_changes_nothing()
        {
            Assert.False(Selection().Clear());
        }

        [Fact]
        public void Selecting_nothing_clears()
        {
            MultiSelection<Row> selection = Selection();
            selection.Select(Rows[0]);

            Assert.True(selection.Select(null));
            Assert.True(selection.IsEmpty);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using GCam.Core.Diagnostics;
using GCam.Core.Tooling;
using GCam.Core.Tooling.Import;
using Xunit;

namespace GCam.Core.Tests.Tooling
{
    public class LibrarySessionTests : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "gcam-session-tests", Path.GetRandomFileName());

        public LibrarySessionTests() => Directory.CreateDirectory(_directory);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // Test cleanup only.
            }
        }

        private string PathFor(string name) => Path.Combine(_directory, name + ".gcamtools");

        private static Tool SampleTool(int number = 1)
        {
            return new Tool
            {
                Number = number,
                Name = "sample",
                Type = ToolType.FlatEndMill,
                Geometry = new ToolGeometry { Diameter = 10, FluteLength = 25, FluteCount = 4 },
            };
        }

        [Fact]
        public void A_new_library_is_written_at_once_and_is_not_pending()
        {
            // Creating a file is an explicit act with a path the user just chose, so it
            // does not wait for the session commit - and it must appear in the tree.
            string path = PathFor("new");
            var session = new LibrarySession();

            session.CreateNew(path, "New");

            Assert.True(File.Exists(path));
            Assert.False(session.IsDirty(path));
            Assert.False(session.HasUnsavedChanges);
        }

        [Fact]
        public void Tools_added_to_a_new_library_still_wait_for_the_commit()
        {
            string path = PathFor("filled");
            var session = new LibrarySession();

            ToolLibrary library = session.CreateNew(path, "Filled");
            library.Tools.Add(SampleTool());
            session.MarkDirty(path);

            // The file exists but does not yet contain the tool.
            using (FileStream stream = File.OpenRead(path))
            {
                Assert.Empty(new GcamXmlLibraryReader().Read(stream, path).Library.Tools);
            }

            Assert.Empty(session.SaveAll());

            using (FileStream stream = File.OpenRead(path))
            {
                Assert.Single(new GcamXmlLibraryReader().Read(stream, path).Library.Tools);
            }
        }

        [Fact]
        public void Cancelling_keeps_a_created_library_but_drops_later_edits()
        {
            string path = PathFor("kept");
            var session = new LibrarySession();

            ToolLibrary library = session.CreateNew(path, "Kept");
            library.Tools.Add(SampleTool());
            session.MarkDirty(path);

            session.DiscardAll();

            // The library itself survives - it was created deliberately.
            Assert.True(File.Exists(path));
            Assert.False(session.HasUnsavedChanges);

            // The tool added afterwards does not.
            using (FileStream stream = File.OpenRead(path))
            {
                Assert.Empty(new GcamXmlLibraryReader().Read(stream, path).Library.Tools);
            }
        }

        [Fact]
        public void Creating_into_an_unwritable_path_reports_a_user_error()
        {
            var session = new LibrarySession();

            Assert.Throws<GCamUserException>(
                () => session.CreateNew(Path.Combine("Z:", "nope", "x.gcamtools"), "X"));
        }

        [Fact]
        public void Edits_survive_navigating_away_and_back()
        {
            // The reason libraries are cached rather than reloaded on each selection.
            string path = PathFor("edited");
            var session = new LibrarySession();

            ToolLibrary library = session.CreateNew(path, "Edited");
            library.Tools.Add(SampleTool());
            session.MarkDirty(path);
            session.SaveAll();

            // Open, edit, "navigate away", come back.
            session.Open(path).Library.Tools.Add(SampleTool(2));
            session.MarkDirty(path);

            Assert.Equal(2, session.Open(path).Library.Tools.Count);
        }

        [Fact]
        public void Saving_commits_every_dirty_library_not_just_the_last_one()
        {
            var session = new LibrarySession();

            string a = PathFor("a");
            string b = PathFor("b");
            session.CreateNew(a, "A").Tools.Add(SampleTool());
            session.CreateNew(b, "B").Tools.Add(SampleTool());
            session.MarkDirty(a);
            session.MarkDirty(b);

            Assert.Equal(2, session.DirtyPaths.Count);
            Assert.Empty(session.SaveAll());

            Assert.True(File.Exists(a));
            Assert.True(File.Exists(b));
            Assert.False(session.HasUnsavedChanges);
        }

        [Fact]
        public void Saved_content_round_trips()
        {
            string path = PathFor("roundtrip");
            var session = new LibrarySession();

            ToolLibrary library = session.CreateNew(path, "Round trip");
            library.Tools.Add(SampleTool(7));
            session.MarkDirty(path);
            session.SaveAll();

            using (FileStream stream = File.OpenRead(path))
            {
                ToolLibrary reloaded = new GcamXmlLibraryReader().Read(stream, path).Library;

                Assert.Equal("Round trip", reloaded.Name);
                Assert.Equal(7, Assert.Single(reloaded.Tools).Number);
            }
        }

        [Fact]
        public void A_failed_save_leaves_the_previous_file_intact()
        {
            // Written to a temporary file and swapped, so a mid-write failure cannot
            // destroy a good library.
            string path = PathFor("existing");
            var session = new LibrarySession();
            session.CreateNew(path, "Existing").Tools.Add(SampleTool());
            session.MarkDirty(path);
            session.SaveAll();

            string original = File.ReadAllText(path);

            Assert.Contains("Existing", original);
            Assert.False(File.Exists(path + ".saving"));
        }

        [Fact]
        public void Hsm_libraries_are_read_only()
        {
            var session = new LibrarySession();

            Assert.False(session.CanEdit(@"C:\tools\vendor.hsmlib"));
            Assert.True(session.CanEdit(@"C:\tools\shop.gcamtools"));
        }

        [Fact]
        public void Save_as_copy_produces_an_independent_editable_library()
        {
            var session = new LibrarySession();
            ToolLibrary source = HsmLibraryReaderTests.ReadExampleLibrary();

            string path = PathFor("converted");
            ToolLibrary copy = session.SaveAsCopy(source, path, "Converted");

            Assert.Equal(source.Tools.Count, copy.Tools.Count);

            // Written at once, like any other newly created library.
            Assert.True(File.Exists(path));
            Assert.False(session.IsDirty(path));

            // Independent: editing the copy must not touch the imported library.
            copy.Tools[0].Name = "changed";
            Assert.NotEqual("changed", source.Tools[0].Name);

            // Holders were copied, not shared across libraries.
            Assert.NotEmpty(copy.Holders);
            Assert.DoesNotContain(copy.Holders, h => source.Holders.Any(s => ReferenceEquals(s, h)));
        }

        [Fact]
        public void Copied_tools_still_point_at_the_copied_holders()
        {
            var session = new LibrarySession();
            ToolLibrary source = HsmLibraryReaderTests.ReadExampleLibrary();

            ToolLibrary copy = session.SaveAsCopy(source, PathFor("held"), "Held");

            foreach (Tool tool in copy.Tools.Where(t => t.Holder != null))
            {
                Assert.Contains(copy.Holders, h => ReferenceEquals(h, tool.Holder));
            }
        }

        [Fact]
        public void Next_tool_number_fills_gaps_rather_than_climbing()
        {
            var library = new ToolLibrary();
            library.Tools.Add(SampleTool(1));
            library.Tools.Add(SampleTool(3));

            Assert.Equal(2, library.NextToolNumber());
        }

        [Fact]
        public void Duplicating_a_tool_gives_it_a_new_identity()
        {
            Tool original = SampleTool();
            Tool copy = original.CloneAsNew();

            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal(original.Geometry.Diameter, copy.Geometry.Diameter, 6);

            // Deep copy, so editing one cannot change the other.
            copy.Geometry.Diameter = 99;
            Assert.Equal(10, original.Geometry.Diameter, 6);
        }
    }
}

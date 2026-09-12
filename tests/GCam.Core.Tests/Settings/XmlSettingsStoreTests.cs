using System.IO;
using System.Linq;
using GCam.Core.Settings;
using Xunit;

namespace GCam.Core.Tests.Settings
{
    public class XmlSettingsStoreTests : System.IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "gcam-settings-tests", Path.GetRandomFileName());

        private string SettingsPath => Path.Combine(_directory, "settings.xml");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Test cleanup only.
            }
        }

        [Fact]
        public void Round_trips_the_folder_list()
        {
            var store = new XmlSettingsStore(SettingsPath);
            store.ToolLibraryFolders.Add(@"C:\Tools\Shop");
            store.ToolLibraryFolders.Add(@"\\fs\cam\libraries");
            store.Save();

            XmlSettingsStore reloaded = XmlSettingsStore.Load(SettingsPath);

            Assert.Equal(
                new[] { @"C:\Tools\Shop", @"\\fs\cam\libraries" },
                reloaded.ToolLibraryFolders.ToArray());
        }

        [Fact]
        public void Order_is_preserved_because_it_is_the_users()
        {
            var store = new XmlSettingsStore(SettingsPath);
            store.ToolLibraryFolders.Add("z");
            store.ToolLibraryFolders.Add("a");
            store.ToolLibraryFolders.Add("m");
            store.Save();

            Assert.Equal(new[] { "z", "a", "m" }, XmlSettingsStore.Load(SettingsPath).ToolLibraryFolders.ToArray());
        }

        [Fact]
        public void Save_creates_the_directory_if_it_does_not_exist()
        {
            Assert.False(Directory.Exists(_directory));

            var store = new XmlSettingsStore(SettingsPath);
            store.ToolLibraryFolders.Add("x");
            store.Save();

            Assert.True(File.Exists(SettingsPath));
        }

        [Fact]
        public void Missing_file_gives_empty_defaults_rather_than_failing()
        {
            XmlSettingsStore store = XmlSettingsStore.Load(SettingsPath);

            Assert.Empty(store.ToolLibraryFolders);
        }

        [Fact]
        public void Corrupt_file_gives_empty_defaults_rather_than_failing()
        {
            // Settings are a convenience; a truncated file must not stop the add-in loading.
            Directory.CreateDirectory(_directory);
            File.WriteAllText(SettingsPath, "<gcamSettings><toolLibraryFolders>");

            XmlSettingsStore store = XmlSettingsStore.Load(SettingsPath);

            Assert.Empty(store.ToolLibraryFolders);
        }

        [Fact]
        public void A_file_that_is_not_ours_is_ignored()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(SettingsPath, "<someoneElsesConfig><folder path='x'/></someoneElsesConfig>");

            Assert.Empty(XmlSettingsStore.Load(SettingsPath).ToolLibraryFolders);
        }

        [Fact]
        public void Saving_to_an_unwritable_path_does_not_throw()
        {
            // Losing preferences is an annoyance, not a crash.
            var store = new XmlSettingsStore(Path.Combine("Z:", "nope", "settings.xml"));
            store.ToolLibraryFolders.Add("x");

            store.Save();
        }

        [Fact]
        public void Blank_entries_are_not_written_out()
        {
            var store = new XmlSettingsStore(SettingsPath);
            store.ToolLibraryFolders.Add("real");
            store.ToolLibraryFolders.Add("   ");
            store.Save();

            Assert.Equal(new[] { "real" }, XmlSettingsStore.Load(SettingsPath).ToolLibraryFolders.ToArray());
        }
    }
}

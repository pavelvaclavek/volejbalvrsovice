using System.IO;
using VolejbalVrsovice.Data.Storage;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Storage
{
    public class DataFolderTests
    {
        [Fact]
        public void Creates_the_data_folder_and_reports_it_writable()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);

                var writable = DataFolder.TryEnsureWritable(paths, out var error);

                Assert.True(writable);
                Assert.Null(error);
                Assert.True(Directory.Exists(paths.DataDirectory));
            }
        }

        [Fact]
        public void Leaves_no_probe_file_behind()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);

                DataFolder.TryEnsureWritable(paths, out _);

                Assert.Empty(Directory.GetFileSystemEntries(paths.DataDirectory));
            }
        }

        [Fact]
        public void Reports_failure_when_the_data_folder_cannot_be_created()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);

                // Na místě složky Data leží soubor – složku nelze založit ani do ní zapisovat.
                File.WriteAllText(paths.DataDirectory, "není složka");

                var writable = DataFolder.TryEnsureWritable(paths, out var error);

                Assert.False(writable);
                Assert.False(string.IsNullOrEmpty(error));
            }
        }

        [Fact]
        public void Paths_are_derived_from_the_application_folder()
        {
            var paths = new AppPaths(@"C:\Aplikace\VolejbalVrsovice");

            Assert.Equal(@"C:\Aplikace\VolejbalVrsovice\Data", paths.DataDirectory);
            Assert.Equal(@"C:\Aplikace\VolejbalVrsovice\Data\logs", paths.LogsDirectory);
            Assert.Equal(@"C:\Aplikace\VolejbalVrsovice\Data\volejbal.lock", paths.LockFile);
            Assert.Equal(@"C:\Aplikace\VolejbalVrsovice\Data\volejbal.lock.info", paths.LockInfoFile);
        }
    }
}

using System;
using System.IO;
using VolejbalVrsovice.Data.Storage;
using VolejbalVrsovice.Tests.TestSupport;
using Xunit;

namespace VolejbalVrsovice.Tests.Storage
{
    public class InstanceLockTests
    {
        private static readonly LockHolderInfo FirstUser =
            new LockHolderInfo("PC-POKLADNA", "jnovak", new DateTimeOffset(2026, 3, 5, 14, 7, 0, TimeSpan.FromHours(1)));

        private static readonly LockHolderInfo SecondUser =
            new LockHolderInfo("PC-TRENER", "psvoboda", new DateTimeOffset(2026, 3, 5, 15, 0, 0, TimeSpan.FromHours(1)));

        [Fact]
        public void First_instance_acquires_the_lock()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);

                var result = InstanceLock.TryAcquire(paths, FirstUser);

                using (result.Lock)
                {
                    Assert.True(result.Acquired);
                    Assert.Null(result.Holder);
                    Assert.True(File.Exists(paths.LockFile));
                    Assert.True(File.Exists(paths.LockInfoFile));
                }
            }
        }

        [Fact]
        public void Second_instance_is_rejected_and_learns_who_holds_the_lock()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);

                using (InstanceLock.TryAcquire(paths, FirstUser).Lock)
                {
                    var second = InstanceLock.TryAcquire(paths, SecondUser);

                    Assert.False(second.Acquired);
                    Assert.Null(second.Lock);
                    Assert.NotNull(second.Holder);
                    Assert.Equal("PC-POKLADNA", second.Holder.MachineName);
                    Assert.Equal("jnovak", second.Holder.UserName);
                    Assert.Equal(FirstUser.StartedAt, second.Holder.StartedAt);
                }
            }
        }

        [Fact]
        public void Rejected_instance_does_not_overwrite_holder_info()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);

                using (InstanceLock.TryAcquire(paths, FirstUser).Lock)
                {
                    InstanceLock.TryAcquire(paths, SecondUser);
                    var third = InstanceLock.TryAcquire(paths, SecondUser);

                    Assert.Equal("jnovak", third.Holder.UserName);
                }
            }
        }

        [Fact]
        public void Lock_is_free_again_after_the_holder_exits()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);

                InstanceLock.TryAcquire(paths, FirstUser).Lock.Dispose();

                Assert.False(File.Exists(paths.LockFile));
                Assert.False(File.Exists(paths.LockInfoFile));

                var next = InstanceLock.TryAcquire(paths, SecondUser);
                using (next.Lock)
                {
                    Assert.True(next.Acquired);
                }
            }
        }

        [Fact]
        public void Leftover_files_after_a_crash_do_not_block_the_next_start()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);
                Directory.CreateDirectory(paths.DataDirectory);

                // Po pádu procesu zůstanou soubory na disku, ale nikdo je nedrží otevřené.
                File.WriteAllText(paths.LockFile, string.Empty);
                File.WriteAllText(paths.LockInfoFile, "machine=PC-STARY\nuser=nekdo\nstarted=2020-01-01T00:00:00.0000000+01:00\n");

                var result = InstanceLock.TryAcquire(paths, SecondUser);
                using (result.Lock)
                {
                    Assert.True(result.Acquired);
                }
            }
        }

        [Fact]
        public void Busy_lock_without_readable_info_is_still_reported_as_busy()
        {
            using (var temp = new TempDirectory())
            {
                var paths = new AppPaths(temp.Path);

                using (InstanceLock.TryAcquire(paths, FirstUser).Lock)
                {
                    File.Delete(paths.LockInfoFile);

                    var second = InstanceLock.TryAcquire(paths, SecondUser);

                    Assert.False(second.Acquired);
                    Assert.Null(second.Holder);
                }
            }
        }

        [Fact]
        public void Disposing_twice_is_harmless()
        {
            using (var temp = new TempDirectory())
            {
                var acquired = InstanceLock.TryAcquire(new AppPaths(temp.Path), FirstUser).Lock;

                acquired.Dispose();
                acquired.Dispose();
            }
        }
    }
}

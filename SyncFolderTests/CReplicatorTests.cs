using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SyncFolderService.SyncLogic;

namespace SyncFolderTests
{
    public sealed class CReplicatorTests : IDisposable
    {
        private readonly string m_Root;
        private readonly string m_Source;
        private readonly string m_Destination;
        private readonly ILogger<CReplicator> m_Logger = NullLogger<CReplicator>.Instance;

        public CReplicatorTests()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "SyncFolderTests", Guid.NewGuid().ToString("N"));
            m_Source = Path.Combine(m_Root, "Source");
            m_Destination = Path.Combine(m_Root, "Destination");

            Directory.CreateDirectory(m_Source);
        }

        public void Dispose()
        {
            if (!Directory.Exists(m_Root))
            {
                return;
            }

            ClearReadOnlyAttributes(m_Root);
            Directory.Delete(m_Root, true);
        }

        [Fact]
        public void CopiesTheFilesThatAreMissingInTheDestination()
        {
            WriteSourceFile("a.txt", "content");

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(1L, v_Result.Copied);
            Assert.Equal(0L, v_Result.Updated);
            Assert.Equal(0L, v_Result.Deleted);
            Assert.True(v_Result.Success);
            Assert.Equal("content", File.ReadAllText(Path.Combine(m_Destination, "a.txt")));
        }

        [Fact]
        public void CopiesTheFilesOfTheSubFolders()
        {
            WriteSourceFile(GetRelativePath("sub", "b.txt"), "content");
            WriteSourceFile(GetRelativePath("sub", "deeper", "c.txt"), "content");

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(2L, v_Result.Copied);
            Assert.Equal("content", File.ReadAllText(Path.Combine(m_Destination, GetRelativePath("sub", "b.txt"))));
            Assert.Equal("content", File.ReadAllText(Path.Combine(m_Destination, GetRelativePath("sub", "deeper", "c.txt"))));
        }

        [Fact]
        public void UpdatesTheFilesThatAreOlderInTheDestination()
        {
            DateTime v_Now = DateTime.UtcNow;

            WriteSourceFile("a.txt", "new", v_Now.AddMinutes(1));
            WriteDestinationFile("a.txt", "old", v_Now);

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(1L, v_Result.Updated);
            Assert.Equal(0L, v_Result.Copied);
            Assert.Equal("new", File.ReadAllText(Path.Combine(m_Destination, "a.txt")));
        }

        [Fact]
        public void KeepsTheFilesThatAreUpToDate()
        {
            DateTime v_Now = DateTime.UtcNow.AddMinutes(-10);

            WriteSourceFile("a.txt", "source", v_Now);
            WriteDestinationFile("a.txt", "destination", v_Now.AddMinutes(1));

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(0L, v_Result.Updated);
            Assert.Equal(0L, v_Result.Copied);
            Assert.Equal("destination", File.ReadAllText(Path.Combine(m_Destination, "a.txt")));
        }

        [Fact]
        public void RemovesTheFilesThatAreAbsentInTheSource()
        {
            WriteSourceFile("a.txt", "content");
            WriteDestinationFile("a.txt", "content");
            WriteDestinationFile("b.txt", "content");

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(1L, v_Result.Deleted);
            Assert.True(File.Exists(Path.Combine(m_Destination, "a.txt")));
            Assert.False(File.Exists(Path.Combine(m_Destination, "b.txt")));
        }

        [Fact]
        public void RemovesTheFoldersThatAreAbsentInTheSource()
        {
            WriteSourceFile("a.txt", "content");
            WriteDestinationFile(GetRelativePath("sub", "b.txt"), "content");
            WriteDestinationFile(GetRelativePath("sub", "deeper", "c.txt"), "content");

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(1L, v_Result.DeletedFolders);
            Assert.Equal(2L, v_Result.Deleted);
            Assert.False(Directory.Exists(Path.Combine(m_Destination, "sub")));
        }

        [Fact]
        public void KeepsTheEmptyFoldersThatExistInTheSource()
        {
            Directory.CreateDirectory(Path.Combine(m_Source, "empty"));
            WriteDestinationFile(GetRelativePath("empty", "b.txt"), "content");

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(0L, v_Result.DeletedFolders);
            Assert.Equal(1L, v_Result.Deleted);
            Assert.True(Directory.Exists(Path.Combine(m_Destination, "empty")));
            Assert.False(File.Exists(Path.Combine(m_Destination, GetRelativePath("empty", "b.txt"))));
        }

        [Fact]
        public void UpdatesTheReadOnlyFilesOfTheDestination()
        {
            DateTime v_Now = DateTime.UtcNow;
            string v_DestinationFile = WriteDestinationFile("a.txt", "old", v_Now);
            File.SetAttributes(v_DestinationFile, FileAttributes.ReadOnly);

            WriteSourceFile("a.txt", "new", v_Now.AddMinutes(1));

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(1L, v_Result.Updated);
            Assert.Equal(0L, v_Result.Failed);
            Assert.Equal("new", File.ReadAllText(v_DestinationFile));
            Assert.False((File.GetAttributes(v_DestinationFile) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly);
        }

        [Fact]
        public void MatchesTheFilesIgnoringTheCase()
        {
            DateTime v_Now = DateTime.UtcNow;

            WriteSourceFile("Report.txt", "new", v_Now.AddMinutes(1));
            WriteDestinationFile("report.txt", "old", v_Now);

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(1L, v_Result.Updated);
            Assert.Equal(0L, v_Result.Copied);
            Assert.Equal("new", File.ReadAllText(Path.Combine(m_Destination, "report.txt")));
            Assert.Single(Directory.GetFiles(m_Destination));
        }

        [Fact]
        public void CreatesTheDestinationFolderWhenItIsMissing()
        {
            WriteSourceFile("a.txt", "content");

            Assert.False(Directory.Exists(m_Destination));

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.True(Directory.Exists(m_Destination));
            Assert.Equal(1L, v_Result.Copied);
        }

        [Fact]
        public void ThrowsWhenTheSourceFolderIsMissing()
        {
            CReplicator v_Replicator = new CReplicator(Path.Combine(m_Root, "missing"), m_Destination, m_Logger);

            Assert.Throws<DirectoryNotFoundException>(() => v_Replicator.ReplicateAsync());
        }

        [Fact]
        public void CountsTheCopiedUpdatedAndDeletedFiles()
        {
            DateTime v_Now = DateTime.UtcNow;

            WriteSourceFile("copy.txt", "content", v_Now);
            WriteSourceFile("update.txt", "new", v_Now.AddMinutes(1));
            WriteSourceFile("keep.txt", "content", v_Now);

            WriteDestinationFile("update.txt", "old", v_Now);
            WriteDestinationFile("keep.txt", "content", v_Now.AddMinutes(1));
            WriteDestinationFile("delete.txt", "content", v_Now);

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(1L, v_Result.Copied);
            Assert.Equal(1L, v_Result.Updated);
            Assert.Equal(1L, v_Result.Deleted);
            Assert.Equal(3L, v_Result.Total);
            Assert.True(v_Result.Success);
        }

        [Fact]
        public void DoesNotRemoveTheFoldersWithASharedPrefix()
        {
            WriteSourceFile(GetRelativePath("sub2", "keep.txt"), "content");
            WriteDestinationFile(GetRelativePath("sub2", "keep.txt"), "content");
            WriteDestinationFile(GetRelativePath("sub", "remove.txt"), "content");

            SyncResult v_Result = CreateReplicator().ReplicateAsync();

            Assert.Equal(1L, v_Result.DeletedFolders);
            Assert.False(Directory.Exists(Path.Combine(m_Destination, "sub")));
            Assert.True(Directory.Exists(Path.Combine(m_Destination, "sub2")));
            Assert.True(File.Exists(Path.Combine(m_Destination, GetRelativePath("sub2", "keep.txt"))));
        }

        private CReplicator CreateReplicator()
        {
            return new CReplicator(m_Source, m_Destination, m_Logger);
        }

        private string WriteSourceFile(string p_RelativePath, string p_Content, DateTime? p_LastWriteTimeUtc = null)
        {
            return WriteFile(Path.Combine(m_Source, p_RelativePath), p_Content, p_LastWriteTimeUtc);
        }

        private string WriteDestinationFile(string p_RelativePath, string p_Content, DateTime? p_LastWriteTimeUtc = null)
        {
            return WriteFile(Path.Combine(m_Destination, p_RelativePath), p_Content, p_LastWriteTimeUtc);
        }

        private static string WriteFile(string p_Path, string p_Content, DateTime? p_LastWriteTimeUtc)
        {
            string? v_Folder = Path.GetDirectoryName(p_Path);

            if (!string.IsNullOrEmpty(v_Folder))
            {
                Directory.CreateDirectory(v_Folder);
            }

            File.WriteAllText(p_Path, p_Content);

            if (p_LastWriteTimeUtc.HasValue)
            {
                File.SetLastWriteTimeUtc(p_Path, p_LastWriteTimeUtc.Value);
            }

            return p_Path;
        }

        private static string GetRelativePath(params string[] p_Parts)
        {
            return Path.Combine(p_Parts);
        }

        private static void ClearReadOnlyAttributes(string p_Folder)
        {
            foreach (string v_File in Directory.EnumerateFiles(p_Folder, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0
            }))
            {
                FileAttributes v_Attributes = File.GetAttributes(v_File);

                if ((v_Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(v_File, v_Attributes & ~FileAttributes.ReadOnly);
                }
            }
        }
    }
}

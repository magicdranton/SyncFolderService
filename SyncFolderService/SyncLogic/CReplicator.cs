using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SyncFolderService.SyncLogic
{
    /// <summary>
    /// Synchronizes the content of a source folder into a destination folder.
    /// Files that are missing in the destination are copied, files that are older are updated and
    /// files and folders that are absent in the source are removed from the destination.
    /// </summary>
    public sealed class CReplicator
    {
        private static readonly EnumerationOptions FileEnumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        private static readonly EnumerationOptions RecursiveEnumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        private readonly ILogger<CReplicator> m_Logger;
        private string m_Source;
        private string m_Destination;

        public CReplicator(IOptions<SyncFolderServiceOptions> SyncOptions, ILogger<CReplicator> logger)
        {
            var options = SyncOptions.Value;
            ArgumentException.ThrowIfNullOrWhiteSpace(options.SourcePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.DestinationPath);
            ArgumentNullException.ThrowIfNull(logger);

            m_Logger = logger;
            m_Source = Path.GetFullPath(options.SourcePath);
            m_Destination = Path.GetFullPath(options.DestinationPath);            
        }

        /// <summary>
        /// Full path of the source folder.
        /// </summary>
        public string Source => m_Source;

        /// <summary>
        /// Full path of the destination folder.
        /// </summary>
        public string Destination => m_Destination;

        /// <summary>
        /// Runs a single synchronization and returns its summary.
        /// </summary>
        public async Task<SyncResult> ReplicateAsync(CancellationToken cancellationToken)
        {
            var v_Counters = new SyncCounters();

            using ScanResult v_Source = await ScanTreeAsync(m_Source, true, cancellationToken);
            using ScanResult v_Destination = await ScanTreeAsync(m_Destination, false, cancellationToken);

            await RemoveOutdatedFoldersAsync(v_Source, v_Destination, v_Counters, cancellationToken);
            await RemoveOutdatedFilesAsync(v_Source, v_Destination, v_Counters, cancellationToken);
            await CopyAndUpdateFilesAsync(v_Source, v_Destination, v_Counters, cancellationToken);

            v_Source.Clear();
            v_Destination.Clear();

            return new SyncResult
            {
                Copied = v_Counters.Copied,
                Updated = v_Counters.Updated,
                Deleted = v_Counters.Deleted,
                DeletedFolders = v_Counters.DeletedFolders,
                Failed = v_Counters.Failed
            };
        }

        /// <summary>
        /// Collects all files and folders of a directory tree. The folders are walked iteratively so
        /// that a deep tree cannot exhaust the call stack.
        /// </summary>
        private Task<ScanResult> ScanTreeAsync(string p_Root, bool p_IsSource, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                m_Logger.LogInformation("Scanning tree... {0}", p_Root);

                var v_Result = new ScanResult();

                if (!Directory.Exists(p_Root))
                {
                    if (p_IsSource)
                    {
                        throw new DirectoryNotFoundException($"The source folder '{p_Root}' does not exist.");
                    }

                    m_Logger.LogInformation("The destination folder '{Folder}' does not exist and is created.", p_Root);
                    Directory.CreateDirectory(p_Root);
                }

                v_Result.Directories.Add(string.Empty);

                var v_Pending = new Queue<string>();
                v_Pending.Enqueue(p_Root);

                while ((v_Pending.Count > 0) && (!cancellationToken.IsCancellationRequested))
                {
                    string v_Current = v_Pending.Dequeue();
                    string v_RelativeFolder = GetRelativeFolder(p_Root, v_Current);

                    try
                    {
                        foreach (string v_File in Directory.EnumerateFiles(v_Current, "*", FileEnumerationOptions))
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            string v_RelativePath = Path.GetRelativePath(p_Root, v_File);

                            if (!v_Result.Files.TryAdd(v_RelativePath, new FileInfo(v_File)))
                            {
                                m_Logger.LogWarning("The path '{Path}' occurs more than once, the last occurrence is used.", v_RelativePath);
                            }                            
                        }

                        foreach (string v_Folder in Directory.EnumerateDirectories(v_Current, "*", FileEnumerationOptions))
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            string v_RelativePath = Path.GetRelativePath(p_Root, v_Folder);

                            // A link is not followed, otherwise a link that points to one of its parents loops forever.
                            // The content of the destination folder is protected, because it cannot be verified.
                            if ((File.GetAttributes(v_Folder) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                            {
                                v_Result.Directories.Add(v_RelativePath);
                                v_Result.UnreadableFolders.Add(v_RelativePath);
                                m_Logger.LogWarning("The folder '{Folder}' is a link and is not followed.", v_Folder);
                                continue;
                            }

                            v_Result.Directories.Add(v_RelativePath);
                            v_Pending.Enqueue(v_Folder);
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        v_Result.UnreadableFolders.Add(v_RelativeFolder);
                        m_Logger.LogWarning(e, "The folder '{Folder}' cannot be read, its content is left untouched.", v_Current);
                    }
                }

                m_Logger.LogInformation("Finished scanning tree {0}", p_Root);

                return v_Result;
            });
        }

        /// <summary>
        /// Removes the folders of the destination that are absent in the source.
        /// </summary>
        private Task RemoveOutdatedFoldersAsync(ScanResult p_Source, ScanResult p_Destination, SyncCounters p_Counters, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                m_Logger.LogInformation("Removing outdated folders...");

                var v_FoldersToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string v_Folder in p_Destination.Directories)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    // The root folder of the destination is never removed.
                    if (v_Folder.Length == 0)
                    {
                        continue;
                    }

                    if (p_Source.Directories.Contains(v_Folder))
                    {
                        continue;
                    }

                    // The source folder could not be read, so the destination content is kept.
                    if (IsProtected(v_Folder, p_Source.UnreadableFolders))
                    {
                        continue;
                    }

                    v_FoldersToDelete.Add(v_Folder);
                }

                foreach (string v_Folder in GetTopMostFolders(v_FoldersToDelete))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    string v_FullPath = Path.Combine(m_Destination, v_Folder);

                    if (!Directory.Exists(v_FullPath))
                    {
                        continue;
                    }

                    ClearReadOnlyAttributes(v_FullPath);

                    try
                    {
                        long v_FilesInFolder = CountFiles(v_FullPath);
                        Directory.Delete(v_FullPath, true);

                        p_Counters.Deleted += v_FilesInFolder;
                        p_Counters.DeletedFolders++;

                        m_Logger.LogInformation("The folder '{Folder}' is absent in the source folder and was removed ({Files} file(s)).", v_FullPath, v_FilesInFolder);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        p_Counters.Failed++;
                        m_Logger.LogWarning(e, "The folder '{Folder}' cannot be removed.", v_FullPath);
                    }
                }

                m_Logger.LogInformation("Finished removing outdated folders.");
            });
        }

        /// <summary>
        /// Removes the files of the destination that are absent in the source.
        /// </summary>
        private Task RemoveOutdatedFilesAsync(ScanResult p_Source, ScanResult p_Destination, SyncCounters p_Counters, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                m_Logger.LogInformation("Removing outdated files...");

                foreach (KeyValuePair<string, FileInfo> v_Entry in p_Destination.Files)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    string v_RelativePath = v_Entry.Key;

                    if (p_Source.Files.ContainsKey(v_RelativePath))
                    {
                        continue;
                    }

                    string v_Folder = GetParentFolderOfFile(v_RelativePath);

                    // The source folder could not be read, so the destination file is kept.
                    if (IsProtected(v_Folder, p_Source.UnreadableFolders))
                    {
                        m_Logger.LogWarning("The file '{File}' is kept because the matching source folder cannot be read.", v_Entry.Value.FullName);
                        continue;
                    }

                    // The whole folder is removed separately when it is absent in the source.
                    if (!p_Source.Directories.Contains(v_Folder))
                    {
                        continue;
                    }

                    // The file was already removed together with its folder.
                    if (!File.Exists(v_Entry.Value.FullName))
                    {
                        continue;
                    }

                    try
                    {
                        File.Delete(v_Entry.Value.FullName);
                        p_Counters.Deleted++;

                        m_Logger.LogInformation("The file '{File}' is absent in the source folder and was removed.", v_Entry.Value.FullName);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        p_Counters.Failed++;
                        m_Logger.LogWarning(e, "The file '{File}' cannot be removed.", v_Entry.Value.FullName);
                    }
                }
                
                m_Logger.LogInformation("Finished removing outdated files.");
            });
        }

        /// <summary>
        /// Copies the new files and updates the outdated ones.
        /// </summary>
        private Task CopyAndUpdateFilesAsync(ScanResult p_Source, ScanResult p_Destination, SyncCounters p_Counters, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                m_Logger.LogInformation("Copying and updating files...");
                foreach (KeyValuePair<string, FileInfo> v_Entry in p_Source.Files)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    string v_RelativePath = v_Entry.Key;
                    FileInfo v_SourceFile = v_Entry.Value;
                    string v_DestinationPath = Path.Combine(m_Destination, v_RelativePath);

                    try
                    {
                        if (p_Destination.Files.TryGetValue(v_RelativePath, out FileInfo v_DestinationFile) && v_DestinationFile.Exists)
                        {
                            // The file already exists, so it is updated only when the source is newer.
                            if (v_DestinationFile.LastWriteTimeUtc >= v_SourceFile.LastWriteTimeUtc)
                            {
                                continue;
                            }

                            // The existing destination file keeps its own name and attributes.
                            v_DestinationPath = v_DestinationFile.FullName;
                            ClearReadOnlyAttribute(v_DestinationPath);

                            File.Copy(v_SourceFile.FullName, v_DestinationPath, true);
                            p_Counters.Updated++;

                            m_Logger.LogDebug("The file '{File}' was updated.", v_DestinationPath);
                        }
                        else
                        {
                            string? v_DestinationFolder = Path.GetDirectoryName(v_DestinationPath);

                            if (!string.IsNullOrEmpty(v_DestinationFolder))
                            {
                                Directory.CreateDirectory(v_DestinationFolder);
                            }

                            File.Copy(v_SourceFile.FullName, v_DestinationPath, true);
                            p_Counters.Copied++;

                            m_Logger.LogDebug("The file '{File}' was copied.", v_DestinationPath);
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        p_Counters.Failed++;
                        m_Logger.LogWarning(e, "The file '{Source}' cannot be copied to '{Destination}'.", v_SourceFile.FullName, v_DestinationPath);
                    }
                }

                m_Logger.LogInformation("Finished copying and updating files.");
            });
        }

        /// <summary>
        /// Returns the folders of the given set that are not covered by another folder of the set.
        /// </summary>
        private static IEnumerable<string> GetTopMostFolders(HashSet<string> p_Folders)
        {
            foreach (string v_Folder in p_Folders)
            {
                bool v_IsCovered = false;
                string? v_Parent = GetParentFolder(v_Folder);

                while (v_Parent is not null)
                {
                    if (p_Folders.Contains(v_Parent))
                    {
                        v_IsCovered = true;
                        break;
                    }

                    v_Parent = GetParentFolder(v_Parent);
                }

                if (!v_IsCovered)
                {
                    yield return v_Folder;
                }
            }
        }

        /// <summary>
        /// Indicates whether the given folder is the given ancestor folder or is located inside of it.
        /// </summary>
        private static bool IsSameOrUnder(string p_Path, string p_Ancestor)
        {
            // The root folder covers every folder.
            if (p_Ancestor.Length == 0)
            {
                return true;
            }

            if (p_Path.Length == 0)
            {
                return false;
            }

            if (string.Equals(p_Path, p_Ancestor, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return p_Path.StartsWith(p_Ancestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Indicates whether the given folder cannot be verified because the matching source folder cannot be read.
        /// </summary>
        private static bool IsProtected(string p_Folder, HashSet<string> p_UnreadableFolders)
        {
            foreach (string v_UnreadableFolder in p_UnreadableFolders)
            {
                if (IsSameOrUnder(p_Folder, v_UnreadableFolder))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the parent folder of a relative path or null when the path is located in the root folder.
        /// </summary>
        private static string? GetParentFolder(string p_RelativePath)
        {
            int v_Index = GetLastSeparatorIndex(p_RelativePath);
            return v_Index <= 0 ? null : p_RelativePath[..v_Index];
        }

        /// <summary>
        /// Returns the folder that contains the given relative path.
        /// </summary>
        private static string GetParentFolderOfFile(string p_RelativePath)
        {
            int v_Index = GetLastSeparatorIndex(p_RelativePath);
            return v_Index < 0 ? string.Empty : p_RelativePath[..v_Index];
        }

        private static int GetLastSeparatorIndex(string p_Path)
        {
            int v_Index = p_Path.LastIndexOf(Path.DirectorySeparatorChar);
            int v_AlternativeIndex = p_Path.LastIndexOf(Path.AltDirectorySeparatorChar);
            return Math.Max(v_Index, v_AlternativeIndex);
        }

        /// <summary>
        /// Returns the relative path of a folder, the root folder is represented by an empty string.
        /// </summary>
        private static string GetRelativeFolder(string p_Root, string p_Folder)
        {
            string v_RelativePath = Path.GetRelativePath(p_Root, p_Folder);
            return v_RelativePath == "." ? string.Empty : v_RelativePath;
        }

        private static long CountFiles(string p_Folder)
        {
            try
            {
                return Directory.EnumerateFiles(p_Folder, "*", RecursiveEnumerationOptions).Count();
            }
            catch (IOException)
            {
                // The deletion below reports the failure.
            }
            catch (UnauthorizedAccessException)
            {
                // The deletion below reports the failure.
            }

            return 0;
        }

        /// <summary>
        /// Removes the read-only attribute from all files of a folder, otherwise the folder cannot be deleted.
        /// </summary>
        private void ClearReadOnlyAttributes(string p_Folder)
        {
            try
            {
                foreach (string v_File in Directory.EnumerateFiles(p_Folder, "*", RecursiveEnumerationOptions))
                {
                    ClearReadOnlyAttribute(v_File);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                m_Logger.LogWarning(e, "The files of the folder '{Folder}' cannot be inspected.", p_Folder);
            }
        }

        /// <summary>
        /// Removes the read-only attribute from a file, otherwise the file cannot be overwritten.
        /// </summary>
        private void ClearReadOnlyAttribute(string p_File)
        {
            try
            {
                FileAttributes v_Attributes = File.GetAttributes(p_File);

                if ((v_Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(p_File, v_Attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                m_Logger.LogWarning(e, "The read-only attribute of the file '{File}' cannot be reset.", p_File);
            }
        }
    }
}

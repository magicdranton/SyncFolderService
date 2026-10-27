using System;
using System.Collections.Generic;
using System.Text;

namespace SyncFolderService.SyncLogic
{
    /// <summary>
    /// Content of a scanned directory tree.
    /// </summary>
    internal sealed class ScanResult : IDisposable
    {
        /// <summary>
        /// Files of the tree, the key is the path relative to the root of the tree.
        /// </summary>
        public Dictionary<string, FileInfo> Files { get; } = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Folders of the tree, the key is the path relative to the root of the tree.
        /// The root folder itself is represented by an empty string.
        /// </summary>
        public HashSet<string> Directories { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Folders that could not be read, the key is the path relative to the root of the tree.
        /// </summary>
        public HashSet<string> UnreadableFolders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void Clear()
        {
            this.Files.Clear();
            this.Directories.Clear();
            this.UnreadableFolders.Clear();
        }

        public void Dispose()
        {
            this.Clear();
        }
    }
}

namespace SyncFolderService.SyncLogic
{
    /// <summary>
    /// Summary of a single synchronization run.
    /// </summary>
    public sealed record SyncResult
    {
        /// <summary>
        /// Number of files that were copied to the destination folder.
        /// </summary>
        public long Copied { get; init; }

        /// <summary>
        /// Number of files that were newer in the source folder and were overwritten in the destination folder.
        /// </summary>
        public long Updated { get; init; }

        /// <summary>
        /// Number of files that were removed from the destination folder.
        /// </summary>
        public long Deleted { get; init; }

        /// <summary>
        /// Number of folders that were removed from the destination folder.
        /// </summary>
        public long DeletedFolders { get; init; }

        /// <summary>
        /// Number of files and folders that could not be processed.
        /// </summary>
        public long Failed { get; init; }

        /// <summary>
        /// Total number of files that were copied, updated or deleted.
        /// </summary>
        public long Total => Copied + Updated + Deleted;

        /// <summary>
        /// Indicates whether the synchronization finished without errors.
        /// </summary>
        public bool Success => Failed == 0;
    }
}

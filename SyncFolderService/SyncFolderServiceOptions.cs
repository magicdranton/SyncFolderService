namespace SyncFolderService
{
    /// <summary>
    /// Configuration of the synchronization, bound from the "SyncFolder" configuration section.
    /// </summary>
    public sealed class SyncFolderServiceOptions
    {
        /// <summary>
        /// Name of the configuration section that holds these options.
        /// </summary>
        public const string SectionName = "SyncFolder";

        /// <summary>
        /// Full path of the folder that is the source of the synchronization.
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// Full path of the folder that is the destination of the synchronization.
        /// </summary>
        public string DestinationPath { get; set; } = string.Empty;

        /// <summary>
        /// Number of minutes between two synchronization runs.
        /// </summary>
        public int IntervalMinutes { get; set; } = 60;
    }
}

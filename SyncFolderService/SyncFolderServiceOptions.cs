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
        /// Number of minutes between two synchronization runs. (deprecated when weekly scheduling is used)
        /// </summary>
        public int IntervalMinutes { get; set; } = 60;

        /// <summary>
        /// Scheduling section for weekly runs.
        /// </summary>
        public SyncFolderScheduleOptions Schedule { get; set; } = new();

        /// <summary>
        /// List of sync tasks to execute.
        /// </summary>
        public Dictionary<string, SyncTaskOptions> TaskList { get; set; } = new();
    }

    /// <summary>
    /// Configuration for a single sync task.
    /// </summary>
    public sealed class SyncTaskOptions
    {
        /// <summary>
        /// Description of the task for logging.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Source folder path.
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Destination folder path.
        /// </summary>
        public string Destination { get; set; } = string.Empty;
    }

    /// <summary>
    /// Weekly scheduling configuration.
    /// </summary>
    public sealed class SyncFolderScheduleOptions
    {
        /// <summary>
        /// When true, run only at scheduled weekly time(s). When false, fall back to IntervalMinutes.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Days of week to run. Defaults to Friday.
        /// </summary>
        public DayOfWeek[] Days { get; set; } = [];

        /// <summary>
        /// Hour of day to run (0-23). Defaults to 1 AM.
        /// </summary>
        public int Hour { get; set; } = 1;

        /// <summary>
        /// Minute of hour to run (0-59). Defaults to 0.
        /// </summary>
        public int Minute { get; set; } = 0;

        /// <summary>
        /// Time zone id to use for determining local time. If null/empty, local time is used.
        /// </summary>
        public string? TimeZoneId { get; set; }
    }
}

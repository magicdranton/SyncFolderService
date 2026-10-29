using Microsoft.Extensions.Options;
using SyncFolderService.SyncLogic;

namespace SyncFolderService
{
    /// <summary>
    /// Runs the synchronization when the service starts and then in a fixed interval.
    /// </summary>
    public sealed class Worker(
        ILogger<Worker> logger,
        IOptions<SyncFolderServiceOptions> options,
        CReplicator replicator,
        TimeProvider timeProvider) : BackgroundService
    {
        private readonly SyncFolderServiceOptions m_Options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!ValidateOptions())
            {
                return;
            }
                    if (m_LastRun == null && WeeklyScheduler.IsTimeForRun(now, m_Options.Schedule))

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunSynchronizationAsync(stoppingToken);

                TimeSpan v_Interval = TimeSpan.FromMinutes(Math.Max(1, m_Options.IntervalMinutes));
                logger.LogInformation("The next synchronization is scheduled in {Interval}.", v_Interval);

                try
                {
                    await Task.Delay(v_Interval, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private bool ValidateOptions()
        {
            if (string.IsNullOrWhiteSpace(m_Options.SourcePath) || string.IsNullOrWhiteSpace(m_Options.DestinationPath))
            {
                logger.LogCritical("The configuration section '{Section}' must contain the 'SourcePath' and the 'DestinationPath' setting.", SyncFolderServiceOptions.SectionName);
                return false;
            }

            if (!Directory.Exists(m_Options.SourcePath))
            {
                logger.LogWarning("The source folder '{Folder}' does not exist, the synchronization is skipped until it appears.", m_Options.SourcePath);
            }

            return true;
        }

        private async Task RunSynchronizationAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("The synchronization started. Source folder: '{Source}'. Destination folder: '{Destination}'.", replicator.Source, replicator.Destination);

            try
            {
                SyncResult v_Result = await replicator.ReplicateAsync(stoppingToken);

                logger.LogInformation(
                    "The synchronization finished. Copied: {Copied}, updated: {Updated}, deleted: {Deleted}, deleted folders: {DeletedFolders}, failed: {Failed}.",
                    v_Result.Copied,
                    v_Result.Updated,
                    v_Result.Deleted,
                    v_Result.DeletedFolders,
                    v_Result.Failed);

                if (!v_Result.Success)
                {
                    logger.LogError("The synchronization finished with {Failed} error(s).", v_Result.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The service is shutting down.
            }
            catch (Exception e)
            {
                logger.LogError(e, "The synchronization failed with an unexpected error.");
            }
        }
    }
}

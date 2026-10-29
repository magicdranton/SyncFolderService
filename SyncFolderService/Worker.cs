using Microsoft.Extensions.Options;
using SyncFolderService.SyncLogic;

namespace SyncFolderService
{
    /// <summary>
    /// Runs the synchronization when the service starts and then in a fixed interval.
    /// </summary>
    public sealed class Worker: BackgroundService
    {
        private SyncFolderServiceOptions m_Options;
        private DateTimeOffset? m_LastRun;

        private ILogger<Worker> m_logger;
        private readonly CReplicator m_Replicator;
        private readonly TimeProvider m_timeProvider;

        private readonly object _optionsLock = new();
        private CancellationTokenSource? _currentCycleCts;
        private readonly IDisposable _optionsSubscription;


        public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<SyncFolderServiceOptions> optionsMonitor,
        CReplicator replicator,
        TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(optionsMonitor);
            ArgumentNullException.ThrowIfNull(replicator);
            ArgumentNullException.ThrowIfNull(timeProvider);
            this.m_logger = logger;
            this.m_Options = optionsMonitor.CurrentValue;

            this.m_Replicator = replicator;
            this.m_timeProvider = timeProvider;

            this._optionsSubscription = optionsMonitor.OnChange<SyncFolderServiceOptions>(options =>
            {
                CancellationTokenSource? cycleToCancel;

                lock (_optionsLock)
                {
                    m_Options = options;
                    cycleToCancel = _currentCycleCts;
                }

                m_logger.LogInformation(
                    "Configuration changed. Restarting worker cycle.");

                cycleToCancel?.Cancel();
            })!;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var cycleCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        stoppingToken);

                SyncFolderServiceOptions options;

                lock (_optionsLock)
                {
                    options = m_Options;
                    _currentCycleCts = cycleCts;
                }

                try
                {
                    m_logger.LogInformation(
                        "Starting worker cycle with current configuration.");

                    await RunConfiguredLoopAsync(cycleCts.Token, stoppingToken);
                }
                catch (OperationCanceledException)
                    when (!stoppingToken.IsCancellationRequested)
                {
                    m_logger.LogInformation(
                        "Worker cycle cancelled due to configuration change.");
                }
                finally
                {
                    lock (_optionsLock)
                    {
                        if (ReferenceEquals(_currentCycleCts, cycleCts))
                        {
                            _currentCycleCts = null;
                        }
                    }
                }
            }
        }

        private async Task RunConfiguredLoopAsync(CancellationToken delayCancellationToken, CancellationToken stoppingToken)
        {
            if (!ValidateOptions())
            {
                return;
            }

            if (m_Options.Schedule != null && m_Options.Schedule.Enabled)
            {
                m_logger.LogInformation(
                    "Weekly scheduling enabled. Days: {Days}, Time: {Hour:00}:{Minute:00}, TimeZone: {TimeZone}",
                    string.Join(",", m_Options.Schedule.Days ?? Array.Empty<DayOfWeek>()),
                    m_Options.Schedule.Hour,
                    m_Options.Schedule.Minute,
                    string.IsNullOrWhiteSpace(m_Options.Schedule.TimeZoneId) ? TimeZoneInfo.Local.Id : m_Options.Schedule.TimeZoneId);

                while (!delayCancellationToken.IsCancellationRequested)
                {
                    delayCancellationToken.ThrowIfCancellationRequested();

                    DateTimeOffset now = m_timeProvider.GetUtcNow();
                    DateTimeOffset next = WeeklyScheduler.GetNextOccurrence(now, m_Options.Schedule);

                    TimeSpan delay = next - now;
                    if (delay < TimeSpan.FromSeconds(1))
                    {
                        delay = TimeSpan.FromMinutes(1);
                    }

                    m_logger.LogInformation("Next scheduled synchronization at {NextRun} (in {Delay}).", next, delay);
                    
                    await Task.Delay(delay, m_timeProvider, delayCancellationToken);                    

                    // After Delay we must wait one more to exactly match the scheduled time, because the delay may be less than the actual time to the next occurrence.

                    DateTimeOffset checkTime = m_timeProvider.GetUtcNow();
                    if (WeeklyScheduler.IsTimeForRun(checkTime, m_Options.Schedule))
                    {
                        // Avoid running multiple times in same minute
                        if (m_LastRun == null || (checkTime - m_LastRun.Value).TotalMinutes >= 1)
                        {
                            await RunSynchronizationAsync(stoppingToken);
                            m_LastRun = checkTime;
                        }
                    }
                }
            }
            else
            {
                m_logger.LogInformation("Using interval-based scheduling. IntervalMinutes: {IntervalMinutes}", m_Options.IntervalMinutes);

                while (!delayCancellationToken.IsCancellationRequested)
                {
                    delayCancellationToken.ThrowIfCancellationRequested();

                    await RunSynchronizationAsync(stoppingToken);

                    TimeSpan v_Interval = TimeSpan.FromMinutes(Math.Max(1, m_Options.IntervalMinutes));
                    m_logger.LogInformation("The next synchronization is scheduled in {Interval}.", v_Interval);

                    await Task.Delay(v_Interval, m_timeProvider, delayCancellationToken);
                }
            }
        }

        private bool ValidateOptions()
        {
            if (string.IsNullOrWhiteSpace(m_Options.SourcePath) || string.IsNullOrWhiteSpace(m_Options.DestinationPath))
            {
                m_logger.LogCritical("The configuration section '{Section}' must contain the 'SourcePath' and the 'DestinationPath' setting.", SyncFolderServiceOptions.SectionName);
                return false;
            }

            if (m_Options.Schedule != null && m_Options.Schedule.Enabled)
            {
                if (m_Options.Schedule.Days == null || m_Options.Schedule.Days.Length == 0)
                {
                    m_logger.LogCritical("Weekly scheduling is enabled but no Days are specified.");
                    return false;
                }

                if (m_Options.Schedule.Hour < 0 || m_Options.Schedule.Hour > 23)
                {
                    m_logger.LogCritical("Schedule.Hour must be between 0 and 23.");
                    return false;
                }

                if (m_Options.Schedule.Minute < 0 || m_Options.Schedule.Minute > 59)
                {
                    m_logger.LogCritical("Schedule.Minute must be between 0 and 59.");
                    return false;
                }
            }

            if (!Directory.Exists(m_Options.SourcePath))
            {
                m_logger.LogWarning("The source folder '{Folder}' does not exist, the synchronization is skipped until it appears.", m_Options.SourcePath);
            }

            return true;
        }

        private async Task RunSynchronizationAsync(CancellationToken stoppingToken)
        {
            m_logger.LogInformation("The synchronization started. Source folder: '{Source}'. Destination folder: '{Destination}'.", m_Replicator.Source, m_Replicator.Destination);

            try
            {
                SyncResult v_Result = await m_Replicator.ReplicateAsync(stoppingToken);

                m_logger.LogInformation(
                    "The synchronization finished. Copied: {Copied}, updated: {Updated}, deleted: {Deleted}, deleted folders: {DeletedFolders}, failed: {Failed}.",
                    v_Result.Copied,
                    v_Result.Updated,
                    v_Result.Deleted,
                    v_Result.DeletedFolders,
                    v_Result.Failed);

                if (!v_Result.Success)
                {
                    m_logger.LogError("The synchronization finished with {Failed} error(s).", v_Result.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The service is shutting down.
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "The synchronization failed with an unexpected error.");
            }
        }

        public override void Dispose()
        {
            this._optionsSubscription?.Dispose();
            base.Dispose();
        }
    }
}

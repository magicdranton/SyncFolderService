using System.Diagnostics;

namespace SyncFolderService
{
    /// <summary>
    /// Creates the Windows Event Log source of the service when it is missing.
    /// Creating a source requires administrative rights, a failure is reported but does not stop the service.
    /// </summary>
    internal static class EventLogSourceInitializer
    {
        /// <summary>
        /// Creates the given source in the given log when it does not exist yet.
        /// Returns null on success or a message that describes the problem.
        /// </summary>
        public static string? TryCreateSource(string p_SourceName, string p_LogName)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                if (!EventLog.SourceExists(p_SourceName))
                {
                    EventLog.CreateEventSource(p_SourceName, p_LogName);
                }

                return null;
            }
            catch (Exception e)
            {
                return $"The Windows Event Log source '{p_SourceName}' cannot be created: {e.Message}. " +
                       "Create it manually (for example with 'new-eventlog' or with the Event Viewer) before the service writes its first entry.";
            }
        }
    }
}

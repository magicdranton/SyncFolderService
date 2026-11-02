# SyncFolderService

A Windows Service that synchronizes the contents of a source folder to a destination folder. Files missing in the destination are copied, newer files overwrite older ones in the destination, and files/folders absent in the source are removed from the destination. 

Key features:
- Runs as a Windows Service with console fallback for non-Windows environments
- Weekly scheduling: runs on configured days/times (defaults to Friday at 01:00 AM) via appsettings; falls back to interval-based execution if scheduling is disabled
- Recursive folder scanning with proper handling of reparse points/symlinks and inaccessible folders
- Logging to Windows Event Log and rolling file logs (Serilog)
- Configurable via `SyncFolder` section in appsettings.json (SourcePath, DestinationPath, Schedule, IntervalMinutes)
- Built on .NET 10 with dependency injection and TimeProvider for testability

The task consists of following steps:
1) Copy CReplicator logic into new SyncFolderService project.
2) Adopt sync logic to use .NET core features (e.g. it seems to be that Hashtable is an obsolete data type)
3) See if there are any data type issues. E.g. what if total file number is more than 2 billion? (int32 max value). If so, change for loop data types to long or ulong OR try using foreach loops
3) Make SyncFolderService make logging into Windows Event Log instead of console output.
	3.1) Logging should be minimal: 
		3.1.1) Log when synchronization starts and ends.
		3.1.2) On sync start: log source and destination folder paths.
		3.1.3) On sync end: log summary of synchronization results: how many files were copied, deleted, or updated.
		3.1.4) Log any errors that occur during synchronization.
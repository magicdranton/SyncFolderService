1) Replicator subfolder contains old .NET framework console app
	1.1) The app takes source and destination folder paths and does dest folder synchronization to source one including subfolders and files.
	1.2) CReplicator class is the main class that handles the synchronization logic.
	1.3) CReplicator uses Hashtables to match files by their full paths betweeen source and destination folders.
2) SyncFolderService is a new empty Windows Service project 
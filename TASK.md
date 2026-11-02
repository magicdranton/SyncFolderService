The task consists of following steps:
1) Append SyncFolderServiceOptions class with new options: TaskList from appsettings.json file. 
2) TaskList is a list of sync tasks to be executed by the service. Each task has a name, source folder path, destination folder path, and description.
3) Update the Worker class to read the TaskList from the configuration and execute each sync task in the list.
4) Tasks must be executed sequentially in the order they are defined in the TaskList. If a task fails, the service should log the error and continue with the next task.
5) Adjust CReplicator class for its object to be re-used for each task in the TaskList, allowing for different source and destination folders for each task. Do not instantiate CReplicator for eack Task
6) Use Task's description for logging purposes to indicate which task is currently being executed.

using System;
using System.Collections.Generic;
using System.Text;

namespace SyncFolderService.SyncLogic
{
    /// <summary>
    /// Counters of a single synchronization run.
    /// </summary>
    internal sealed class SyncCounters
    {
        public long Copied;
        public long Updated;
        public long Deleted;
        public long DeletedFolders;
        public long Failed;
    }
}

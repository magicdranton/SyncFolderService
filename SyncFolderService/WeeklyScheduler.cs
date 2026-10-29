using System;

namespace SyncFolderService
{
    public static class WeeklyScheduler
    {
        public static DateTimeOffset GetNextOccurrence(DateTimeOffset now, SyncFolderScheduleOptions schedule)
        {
            if (schedule.Days == null || schedule.Days.Length == 0)
            {
                schedule = new SyncFolderScheduleOptions();
            }

            int hour = schedule.Hour < 0 ? 0 : schedule.Hour > 23 ? 23 : schedule.Hour;
            int minute = schedule.Minute < 0 ? 0 : schedule.Minute > 59 ? 59 : schedule.Minute;

            TimeZoneInfo timeZone = GetTimeZone(schedule.TimeZoneId);

            DateTimeOffset nowInTz = TimeZoneInfo.ConvertTime(now, timeZone);
            DateTime date = nowInTz.Date;
            TimeSpan timeOfDay = new TimeSpan(hour, minute, 0);

            // Look ahead up to 8 days to find next scheduled day
            for (int dayOffset = 0; dayOffset < 8; dayOffset++)
            {
                DateTime candidateDate = date.AddDays(dayOffset);
                DayOfWeek candidateDay = candidateDate.DayOfWeek;

                foreach (DayOfWeek scheduledDay in schedule.Days)
                {
                    if (candidateDay == scheduledDay)
                    {
                        DateTime candidateLocal = candidateDate + timeOfDay;
                        DateTimeOffset candidateInTz = new DateTimeOffset(candidateLocal, timeZone.GetUtcOffset(candidateLocal));

                        if (candidateInTz > nowInTz)
                        {
                            return TimeZoneInfo.ConvertTime(candidateInTz, timeZone) == candidateInTz 
                                ? candidateInTz 
                                : TimeZoneInfo.ConvertTime(candidateInTz, TimeZoneInfo.Utc);
                        }

                        if (dayOffset == 0 && candidateInTz <= nowInTz)
                        {
                            // If it's the same day and time has passed, skip to next occurrence of this or other days
                            continue;
                        }
                    }
                }
            }

            // Fallback: find next week same day
            DateTime fallbackDate = date;
            for (int dayOffset = 1; dayOffset <= 7; dayOffset++)
            {
                DateTime candidateDate = date.AddDays(dayOffset);
                foreach (DayOfWeek scheduledDay in schedule.Days)
                {
                    if (candidateDate.DayOfWeek == scheduledDay)
                    {
                        DateTime candidateLocal = candidateDate + timeOfDay;
                        return new DateTimeOffset(candidateLocal, timeZone.GetUtcOffset(candidateLocal));
                    }
                }
            }

            // Last resort
            DateTime lastResort = date.AddDays(7);
            DateTime lastResortLocal = lastResort + timeOfDay;
            return new DateTimeOffset(lastResortLocal, timeZone.GetUtcOffset(lastResortLocal));
        }

        public static bool IsTimeForRun(DateTimeOffset now, SyncFolderScheduleOptions schedule)
        {
            if (schedule.Days == null || schedule.Days.Length == 0)
            {
                return false;
            }

            int hour = schedule.Hour < 0 ? 0 : schedule.Hour > 23 ? 23 : schedule.Hour;
            int minute = schedule.Minute < 0 ? 0 : schedule.Minute > 59 ? 59 : schedule.Minute;

            TimeZoneInfo timeZone = GetTimeZone(schedule.TimeZoneId);
            DateTimeOffset nowInTz = TimeZoneInfo.ConvertTime(now, timeZone);

            if (Array.IndexOf(schedule.Days, nowInTz.DayOfWeek) < 0)
            {
                return false;
            }

            return nowInTz.Hour == hour && nowInTz.Minute >= minute;
        }

        private static TimeZoneInfo GetTimeZone(string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                return TimeZoneInfo.Local;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }
    }
}

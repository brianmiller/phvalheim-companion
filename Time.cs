using System;

namespace PhValheimCompanion
{
    public class Time
    {
        public static void GetTime()
        {
            string currentTime;
            TimeSpan currentTimeSpan = DateTime.Now.TimeOfDay;
            currentTime = currentTimeSpan.Hours.ToString() + ":" + currentTimeSpan.Minutes.ToString() + ":" + currentTimeSpan.Seconds.ToString() + ": ";
        }
    }
}
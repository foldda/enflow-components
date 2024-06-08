﻿using System.Collections.Generic;
using Foldda.DataAutomation.Framework;
using System.Threading;
using System;
using System.IO;

namespace Foldda.DataAutomation.Timer
{
    //repeat a number of events within an hour
    public class HourlyTimer : BaseTimer
    {
        public HourlyTimer(ILoggingProvider logger, DirectoryInfo homePath) : base(logger, homePath) { }

        internal override void ResetTimeTable()
        {
            DateTime nowTime = DateTime.Now;

            Log("Hourly timer events are scheduled as the following time - ");
            foreach (DayTimeSetting setting in TimeSettings)
            {
                if (setting.HourlyTime > nowTime)
                {
                    TimeTable.Enqueue(setting.HourlyTime);
                    Log(setting.HourlyTime.ToString("mm:ss"));
                }
                //else, the specific time is already passed, don't add to time-table
            }
        }
    }
}

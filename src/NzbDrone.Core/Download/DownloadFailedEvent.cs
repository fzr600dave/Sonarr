﻿using System;
using System.Collections.Generic;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Download
{
    public class DownloadFailedEvent : IEvent
    {
        public DownloadFailedEvent()
        {
            Data = new Dictionary<string, string>();
        }

        public Int32 SeriesId { get; set; }
        public List<Int32> EpisodeIds { get; set; }
        public QualityModel Quality { get; set; }
        public String SourceTitle { get; set; }
        public String DownloadClient { get; set; }
        public String DownloadId { get; set; }
        public String Message { get; set; }
        public Dictionary<string, string> Data { get; set; }
    }
}
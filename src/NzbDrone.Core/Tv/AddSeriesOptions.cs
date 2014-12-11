using System.Collections.Generic;

namespace NzbDrone.Core.Tv
{
    public class AddSeriesOptions
    {
        public bool SearchForMissingEpisodes { get; set; }
        public bool IgnoreEpisodesWithFiles { get; set; }
        public bool IgnoreEpisodesWithoutFiles { get; set; }
        public List<int> IgnoreSeasons { get; set; }

        public AddSeriesOptions()
        {
            IgnoreSeasons = new List<int>();
        }
    }

    public enum MonitorEpisodeType
    {
        All = 0,
        Missing = 1,
        Future = 2,
        FirstSeason = 3
    }
}


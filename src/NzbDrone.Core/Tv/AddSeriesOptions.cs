namespace NzbDrone.Core.Tv
{
    public class AddSeriesOptions
    {
        public bool SearchForMissingEpisodes { get; set; }
        public MonitorEpisodeType MonitorEpisodes { get; set; }
    }

    public enum MonitorEpisodeType
    {
        All = 0,
        IgnoreMissing = 1,
        FutureOnly = 2,
        FirstSeason = 3,
        LatestSeason = 4
    }
}


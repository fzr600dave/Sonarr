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
        Missing = 1,
        Future = 2,
        FirstSeason = 3
    }
}


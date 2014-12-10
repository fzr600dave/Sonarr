using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Tv.Events
{
    public class SeriesAddedEvent : IEvent
    {
        public Series Series { get; private set; }
        public AddSeriesOptions Options { get; private set; }

        public SeriesAddedEvent(Series series, AddSeriesOptions options)
        {
            Series = series;
            Options = options;
        }
    }
}
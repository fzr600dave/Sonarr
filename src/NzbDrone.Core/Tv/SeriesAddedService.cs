using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public class SeriesAddedService : IHandle<SeriesAddedEvent>,
                                      IHandle<SeriesScannedEvent>
    {
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly ICommandExecutor _commandExecutor;
        private readonly Logger _logger;
        private readonly ICached<AddSeriesOptions> _addSeriesOptionsCache;

        public SeriesAddedService(ISeriesService seriesService,
                                  IEpisodeService episodeService,
                                  ICacheManager cacheManager,
                                  ICommandExecutor commandExecutor,
                                  Logger logger)
        {
            _seriesService = seriesService;
            _episodeService = episodeService;
            _commandExecutor = commandExecutor;
            _logger = logger;
            _addSeriesOptionsCache = cacheManager.GetCache<AddSeriesOptions>(GetType());
        }

        private void UnmonitorEpisodes(IEnumerable<Episode> episodes)
        {
            foreach (var episode in episodes)
            {
                episode.Monitored = false;
            }
        }
        
        private void SetEpisodeMonitoredStatus(Series series, List<Episode> episodes, AddSeriesOptions options)
        {
            _logger.Debug("[{0}] Setting episode monitored status.", series.Title);

            if (options.IgnoreEpisodesWithFiles)
            {
                _logger.Debug("Ignoring Episodes with Files");
                UnmonitorEpisodes(episodes.Where(e => e.HasFile));
            }

            if (options.IgnoreEpisodesWithoutFiles)
            {
                _logger.Debug("Ignoring Episodes without Files");
                UnmonitorEpisodes(episodes.Where(e => !e.HasFile && e.AirDateUtc.HasValue && e.AirDateUtc.Value.Before(DateTime.UtcNow)));
            }

            foreach (var season in series.Seasons)
            {
                if (episodes.Where(e => e.SeasonNumber == season.SeasonNumber).All(e => !e.Monitored) || options.IgnoreSeasons.Contains(season.SeasonNumber))
                {
                    season.Monitored = false;
                }
            }

            _seriesService.UpdateSeries(series);
            _episodeService.UpdateEpisodes(episodes);
        }

        private void SearchForMissingEpisodes(Series series, List<Episode> episodes)
        {
            var missing = episodes.Where(e => e.Monitored && !e.HasFile).GroupBy(e => e.SeasonNumber);

            var missingEpisodeIds = new List<int>();

            foreach (var group in missing)
            {
                var seasonNumber = group.Key;

                if (group.Count() > 1)
                {
                    //season search
                    _commandExecutor.PublishCommand(new SeasonSearchCommand
                                                    {
                                                        SeriesId = series.Id,
                                                        SeasonNumber = seasonNumber
                                                    });
                }

                else
                {
                    missingEpisodeIds.AddRange(group.Select(e => e.Id));                    
                }
            }

            if (missingEpisodeIds.Any())
            {
                _commandExecutor.PublishCommand(new EpisodeSearchCommand
                                                    {
                                                        EpisodeIds = missingEpisodeIds
                                                    });
            }

        }

        public void Handle(SeriesAddedEvent message)
        {
            _addSeriesOptionsCache.Set(message.Series.Id.ToString(), message.Options);
        }

        public void Handle(SeriesScannedEvent message)
        {
            var options = _addSeriesOptionsCache.Find(message.Series.Id.ToString());

            if (options == null)
            {
                _logger.Debug("[{0}] Was not recently added, skipping post-add actions", message.Series.Title);
                return;
            }
            
            var episodes = _episodeService.GetEpisodeBySeries(message.Series.Id);
            SetEpisodeMonitoredStatus(message.Series, episodes, options);
            
            if (options.SearchForMissingEpisodes)
            {
                SearchForMissingEpisodes(message.Series, episodes);
            }
        }
    }
}

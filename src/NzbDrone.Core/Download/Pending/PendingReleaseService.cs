﻿using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Download.Pending
{
    public interface IPendingReleaseService
    {
        void Add(DownloadDecision decision);
        
        List<ReleaseInfo> GetPending();
        List<RemoteEpisode> GetPendingRemoteEpisodes(int seriesId);
        List<Queue.Queue> GetPendingQueue();
        Queue.Queue FindPendingQueueItem(int queueId);
        void RemovePendingQueueItem(int queueId);
        RemoteEpisode OldestPendingRelease(int seriesId, IEnumerable<int> episodeIds);
    }

    public class PendingReleaseService : IPendingReleaseService,
                                         IHandle<SeriesDeletedEvent>,
                                         IHandle<EpisodeGrabbedEvent>,
                                         IHandle<RssSyncCompleteEvent>
    {
        private readonly IPendingReleaseRepository _repository;
        private readonly ISeriesService _seriesService;
        private readonly IParsingService _parsingService;
        private readonly IDelayProfileService _delayProfileService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public PendingReleaseService(IPendingReleaseRepository repository,
                                    ISeriesService seriesService,
                                    IParsingService parsingService,
                                    IDelayProfileService delayProfileService,
                                    IEventAggregator eventAggregator,
                                    Logger logger)
        {
            _repository = repository;
            _seriesService = seriesService;
            _parsingService = parsingService;
            _delayProfileService = delayProfileService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        
        public void Add(DownloadDecision decision)
        {
            var alreadyPending = GetPendingReleases();

            var episodeIds = decision.RemoteEpisode.Episodes.Select(e => e.Id);

            var existingReports = alreadyPending.Where(r => r.RemoteEpisode.Episodes.Select(e => e.Id)
                                                             .Intersect(episodeIds)
                                                             .Any());

            if (existingReports.Any(MatchingReleasePredicate(decision)))
            {
                _logger.Debug("This release is already pending, not adding again");
                return;
            }

            _logger.Debug("Adding release to pending releases");
            Insert(decision);
        }

        public List<ReleaseInfo> GetPending()
        {
            return _repository.All().Select(p => p.Release).ToList();
        }

        public List<RemoteEpisode> GetPendingRemoteEpisodes(int seriesId)
        {
            return _repository.AllBySeriesId(seriesId).Select(GetRemoteEpisode).ToList();
        }

        public List<Queue.Queue> GetPendingQueue()
        {
            var queued = new List<Queue.Queue>();

            foreach (var pendingRelease in GetPendingReleases())
            {
                foreach (var episode in pendingRelease.RemoteEpisode.Episodes)
                {
                    var ect = pendingRelease.Release.PublishDate.AddMinutes(GetDelay(pendingRelease.RemoteEpisode));

                    var queue = new Queue.Queue
                                {
                                    Id = episode.Id ^ (pendingRelease.Id << 16),
                                    Series = pendingRelease.RemoteEpisode.Series,
                                    Episode = episode,
                                    Quality = pendingRelease.RemoteEpisode.ParsedEpisodeInfo.Quality,
                                    Title = pendingRelease.Title,
                                    Size = pendingRelease.RemoteEpisode.Release.Size,
                                    Sizeleft = pendingRelease.RemoteEpisode.Release.Size,
                                    RemoteEpisode = pendingRelease.RemoteEpisode,
                                    Timeleft = ect.Subtract(DateTime.UtcNow),
                                    EstimatedCompletionTime = ect,
                                    Status = "Pending"
                                };
                    queued.Add(queue);
                }
            }

            return queued;
        }

        public Queue.Queue FindPendingQueueItem(int queueId)
        {
            return GetPendingQueue().SingleOrDefault(p => p.Id == queueId);
        }

        public void RemovePendingQueueItem(int queueId)
        {
            var id = FindPendingReleaseId(queueId);

            _repository.Delete(id);
        }

        public RemoteEpisode OldestPendingRelease(int seriesId, IEnumerable<int> episodeIds)
        {
            return GetPendingRemoteEpisodes(seriesId)
                .Where(r => r.Episodes.Select(e => e.Id).Intersect(episodeIds).Any())
                .OrderByDescending(p => p.Release.AgeHours)
                .FirstOrDefault();
        }

        private List<PendingRelease> GetPendingReleases()
        {
            var result = new List<PendingRelease>();

            foreach (var release in _repository.All())
            {
                var remoteEpisode = GetRemoteEpisode(release);

                if (remoteEpisode == null) continue;

                release.RemoteEpisode = remoteEpisode;

                result.Add(release);
            }

            return result;
        }

        private RemoteEpisode GetRemoteEpisode(PendingRelease release)
        {
            var series = _seriesService.GetSeries(release.SeriesId);

            //Just in case the series was removed, but wasn't cleaned up yet (housekeeper will clean it up)
            if (series == null) return null;

            var episodes = _parsingService.GetEpisodes(release.ParsedEpisodeInfo, series, true);

            return new RemoteEpisode
            {
                Series = series,
                Episodes = episodes,
                ParsedEpisodeInfo = release.ParsedEpisodeInfo,
                Release = release.Release
            };
        }

        private void Insert(DownloadDecision decision)
        {
            _repository.Insert(new PendingRelease
            {
                SeriesId = decision.RemoteEpisode.Series.Id,
                ParsedEpisodeInfo = decision.RemoteEpisode.ParsedEpisodeInfo,
                Release = decision.RemoteEpisode.Release,
                Title = decision.RemoteEpisode.Release.Title,
                Added = DateTime.UtcNow
            });

            _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
        }

        private void Delete(PendingRelease pendingRelease)
        {
            _repository.Delete(pendingRelease);
            _eventAggregator.PublishEvent(new PendingReleasesUpdatedEvent());
        }

        private Func<PendingRelease, bool> MatchingReleasePredicate(DownloadDecision decision)
        {
            return p => p.Title == decision.RemoteEpisode.Release.Title &&
                   p.Release.PublishDate == decision.RemoteEpisode.Release.PublishDate &&
                   p.Release.Indexer == decision.RemoteEpisode.Release.Indexer;
        }

        private int GetDelay(RemoteEpisode remoteEpisode)
        {
            var delayProfile = _delayProfileService.AllForTags(remoteEpisode.Series.Tags).OrderBy(d => d.Order).First();

            return delayProfile.GetProtocolDelay(remoteEpisode.Release.DownloadProtocol);
        }

        private void RemoveGrabbed(RemoteEpisode remoteEpisode)
        {
            var pendingReleases = GetPendingReleases();
            var episodeIds = remoteEpisode.Episodes.Select(e => e.Id);

            var existingReports = pendingReleases.Where(r => r.RemoteEpisode.Episodes.Select(e => e.Id)
                                                             .Intersect(episodeIds)
                                                             .Any())
                                                             .ToList();

            if (existingReports.Empty())
            {
                return;
            }

            var profile = remoteEpisode.Series.Profile.Value;

            foreach (var existingReport in existingReports)
            {
                var compare = new QualityModelComparer(profile).Compare(remoteEpisode.ParsedEpisodeInfo.Quality,
                                                                        existingReport.RemoteEpisode.ParsedEpisodeInfo.Quality);

                //Only remove lower/equal quality pending releases
                //It is safer to retry these releases on the next round than remove it and try to re-add it (if its still in the feed)
                if (compare >= 0)
                {
                    _logger.Debug("Removing previously pending release, as it was grabbed.");
                    Delete(existingReport);
                }
            }
        }

        private void RemoveRejected(List<DownloadDecision> rejected)
        {
            _logger.Debug("Removing failed releases from pending");
            var pending = GetPendingReleases();

            foreach (var rejectedRelease in rejected)
            {
                var matching = pending.SingleOrDefault(MatchingReleasePredicate(rejectedRelease));

                if (matching != null)
                {
                    _logger.Debug("Removing previously pending release, as it has now been rejected.");
                    Delete(matching);
                }
            }
        }

        private int FindPendingReleaseId(int queueId)
        {
            return GetPendingReleases().First(p => p.RemoteEpisode.Episodes.Any(e => queueId == (e.Id ^ (p.Id << 16)))).Id;
        }

        public void Handle(SeriesDeletedEvent message)
        {
            _repository.DeleteBySeriesId(message.Series.Id);
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            RemoveGrabbed(message.Episode);
        }

        public void Handle(RssSyncCompleteEvent message)
        {
            RemoveRejected(message.ProcessedDecisions.Rejected);
        }
    }
}

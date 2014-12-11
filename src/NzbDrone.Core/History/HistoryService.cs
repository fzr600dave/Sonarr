﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.History
{
    public interface IHistoryService
    {
        List<History> All();
        void Purge();
        void Trim();
        QualityModel GetBestQualityInHistory(Profile profile, int episodeId);
        PagingSpec<History> Paged(PagingSpec<History> pagingSpec);
        List<History> BetweenDates(DateTime startDate, DateTime endDate, HistoryEventType eventType);
        List<History> Grabbed();
        List<History> Imported();
        History MostRecentForEpisode(int episodeId);
        History MostRecentForDownloadId(string downloadId);
        History Get(int historyId);
        List<History> FindBySourceTitle(string sourceTitle);
        List<History> Find(string downloadId, HistoryEventType eventType);
    }

    public class HistoryService : IHistoryService,
                                  IHandle<EpisodeGrabbedEvent>,
                                  IHandle<EpisodeImportedEvent>,
                                  IHandle<DownloadFailedEvent>,
                                  IHandle<EpisodeFileDeletedEvent>
    {
        private readonly IHistoryRepository _historyRepository;
        private readonly Logger _logger;

        public HistoryService(IHistoryRepository historyRepository, Logger logger)
        {
            _historyRepository = historyRepository;
            _logger = logger;
        }

        public List<History> All()
        {
            return _historyRepository.All().ToList();
        }

        public PagingSpec<History> Paged(PagingSpec<History> pagingSpec)
        {
            return _historyRepository.GetPaged(pagingSpec);
        }

        public List<History> BetweenDates(DateTime startDate, DateTime endDate, HistoryEventType eventType)
        {
            return _historyRepository.BetweenDates(startDate, endDate, eventType);
        }

        public List<History> Failed()
        {
            return _historyRepository.Failed();
        }

        public List<History> Grabbed()
        {
            return _historyRepository.Grabbed();
        }

        public List<History> Imported()
        {
            return _historyRepository.Imported();
        }

        public History MostRecentForEpisode(int episodeId)
        {
            return _historyRepository.MostRecentForEpisode(episodeId);
        }

        public History MostRecentForDownloadId(string downloadId)
        {
            return _historyRepository.MostRecentForDownloadId(downloadId);
        }

        public History Get(int historyId)
        {
            return _historyRepository.Get(historyId);
        }

        public List<History> FindBySourceTitle(string sourceTitle)
        {
            return _historyRepository.FindBySourceTitle(sourceTitle);
        }

        public List<History> Find(string downloadId, HistoryEventType eventType)
        {
            return _historyRepository.FindByDownloadId(downloadId).Where(c => c.EventType == eventType).ToList();
        }

        public void Purge()
        {
            _historyRepository.Purge();
        }

        public virtual void Trim()
        {
            _historyRepository.Trim();
        }

        public QualityModel GetBestQualityInHistory(Profile profile, int episodeId)
        {
            var comparer = new QualityModelComparer(profile);
            return _historyRepository.GetBestQualityInHistory(episodeId)
                .OrderByDescending(q => q, comparer)
                .FirstOrDefault();
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            foreach (var episode in message.Episode.Episodes)
            {
                var history = new History
                {
                    EventType = HistoryEventType.Grabbed,
                    Date = DateTime.UtcNow,
                    Quality = message.Episode.ParsedEpisodeInfo.Quality,
                    SourceTitle = message.Episode.Release.Title,
                    SeriesId = episode.SeriesId,
                    EpisodeId = episode.Id,
                    DownloadId = message.DownloadId
                };

                history.Data.Add("Indexer", message.Episode.Release.Indexer);
                history.Data.Add("NzbInfoUrl", message.Episode.Release.InfoUrl);
                history.Data.Add("ReleaseGroup", message.Episode.ParsedEpisodeInfo.ReleaseGroup);
                history.Data.Add("Age", message.Episode.Release.Age.ToString());
                history.Data.Add("AgeHours", message.Episode.Release.AgeHours.ToString());
                history.Data.Add("PublishedDate", message.Episode.Release.PublishDate.ToString("s") + "Z");
                history.Data.Add("DownloadClient", message.DownloadClient);

                if (!message.Episode.ParsedEpisodeInfo.ReleaseHash.IsNullOrWhiteSpace())
                {
                    history.Data.Add("ReleaseHash", message.Episode.ParsedEpisodeInfo.ReleaseHash);
                }

                _historyRepository.Insert(history);
            }
        }

        public void Handle(EpisodeImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            foreach (var episode in message.EpisodeInfo.Episodes)
            {
                var history = new History
                    {
                        EventType = HistoryEventType.DownloadFolderImported,
                        Date = DateTime.UtcNow,
                        Quality = message.EpisodeInfo.Quality,
                        SourceTitle = message.ImportedEpisode.SceneName ?? Path.GetFileNameWithoutExtension(message.EpisodeInfo.Path),
                        SeriesId = message.ImportedEpisode.SeriesId,
                        EpisodeId = episode.Id,
                        DownloadId = message.DownloadId
                    };

                //Won't have a value since we publish this event before saving to DB.
                //history.Data.Add("FileId", message.ImportedEpisode.Id.ToString());
                history.Data.Add("DroppedPath", message.EpisodeInfo.Path);
                history.Data.Add("ImportedPath", Path.Combine(message.EpisodeInfo.Series.Path, message.ImportedEpisode.RelativePath));
                history.Data.Add("DownloadClient", message.DownloadClient);

                _historyRepository.Insert(history);
            }
        }

        public void Handle(DownloadFailedEvent message)
        {
            foreach (var episodeId in message.EpisodeIds)
            {
                var history = new History
                {
                    EventType = HistoryEventType.DownloadFailed,
                    Date = DateTime.UtcNow,
                    Quality = message.Quality,
                    SourceTitle = message.SourceTitle,
                    SeriesId = message.SeriesId,
                    EpisodeId = episodeId,
                    DownloadId = message.DownloadId
                };

                history.Data.Add("DownloadClient", message.DownloadClient);
                history.Data.Add("Message", message.Message);

                _historyRepository.Insert(history);
            }
        }

        public void Handle(EpisodeFileDeletedEvent message)
        {
            if (message.Reason == DeleteMediaFileReason.NoLinkedEpisodes)
            {
                _logger.Debug("Removing episode file from DB as part of cleanup routine.");
                return;
            }

            foreach (var episode in message.EpisodeFile.Episodes.Value)
            {
                var history = new History
                {
                    EventType = HistoryEventType.EpisodeFileDeleted,
                    Date = DateTime.UtcNow,
                    Quality = message.EpisodeFile.Quality,
                    SourceTitle = message.EpisodeFile.Path,
                    SeriesId = message.EpisodeFile.SeriesId,
                    EpisodeId = episode.Id,
                };

                history.Data.Add("Reason", message.Reason.ToString());

                _historyRepository.Insert(history);
            }
        }
    }
}
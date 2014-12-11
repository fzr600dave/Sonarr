﻿using System;
using System.Collections.Generic;
using System.Linq;
using Marr.Data.QGen;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.History
{
    public interface IHistoryRepository : IBasicRepository<History>
    {
        void Trim();
        List<QualityModel> GetBestQualityInHistory(int episodeId);
        List<History> BetweenDates(DateTime startDate, DateTime endDate, HistoryEventType eventType);
        List<History> Failed();
        List<History> Grabbed();
        List<History> Imported();
        History MostRecentForEpisode(int episodeId);
        List<History> FindBySourceTitle(string sourceTitle);
        History MostRecentForDownloadId(string downloadId);
        List<History> FindByDownloadId(string downloadId);
    }

    public class HistoryRepository : BasicRepository<History>, IHistoryRepository
    {
        private readonly IDatabase _database;

        public HistoryRepository(IDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
            _database = database;
        }

        public void Trim()
        {
            var cutoff = DateTime.UtcNow.AddDays(-30).Date;
            Delete(c => c.Date < cutoff);
        }

        public List<QualityModel> GetBestQualityInHistory(int episodeId)
        {
            var history = Query.Where(c => c.EpisodeId == episodeId);

            return history.Select(h => h.Quality).ToList();
        }

        public List<History> BetweenDates(DateTime startDate, DateTime endDate, HistoryEventType eventType)
        {
            return Query.Join<History, Series>(JoinType.Inner, h => h.Series, (h, s) => h.SeriesId == s.Id)
                        .Join<History, Episode>(JoinType.Inner, h => h.Episode, (h, e) => h.EpisodeId == e.Id)
                        .Where(h => h.Date >= startDate)
                        .AndWhere(h => h.Date <= endDate)
                        .AndWhere(h => h.EventType == eventType);
        }

        public List<History> Failed()
        {
            return Query.Where(h => h.EventType == HistoryEventType.DownloadFailed);
        }

        public List<History> Grabbed()
        {
            return Query.Where(h => h.EventType == HistoryEventType.Grabbed);
        }

        public List<History> Imported()
        {
            return Query.Where(h => h.EventType == HistoryEventType.DownloadFolderImported);
        }

        public History MostRecentForEpisode(int episodeId)
        {
            return Query.Where(h => h.EpisodeId == episodeId)
                        .OrderByDescending(h => h.Date)
                        .FirstOrDefault();
        }

        public List<History> FindBySourceTitle(string sourceTitle)
        {
            return Query.Where(h => h.SourceTitle.Contains(sourceTitle));
        }

        public History MostRecentForDownloadId(string downloadId)
        {
            return Query.Where(h => h.DownloadId == downloadId)
             .OrderByDescending(h => h.Date)
             .FirstOrDefault();
        }

        public List<History> FindByDownloadId(string downloadId)
        {
            return Query.Where(h => h.DownloadId == downloadId);
        }

        public List<History> AllForEpisode(int episodeId)
        {
            return Query.Where(h => h.EpisodeId == episodeId);
        }

        protected override SortBuilder<History> GetPagedQuery(QueryBuilder<History> query, PagingSpec<History> pagingSpec)
        {
            var baseQuery = query.Join<History, Series>(JoinType.Inner, h => h.Series, (h, s) => h.SeriesId == s.Id)
                                 .Join<History, Episode>(JoinType.Inner, h => h.Episode, (h, e) => h.EpisodeId == e.Id);

            return base.GetPagedQuery(baseQuery, pagingSpec);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public interface ITrackedDownloadService
    {
        TrackedDownload Find(string downloadId);
        TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem);
        List<TrackedDownload> All();
        List<TrackedDownload> GetActive();
    }

    public class TrackedDownloadService : ITrackedDownloadService, IHandle<SceneMappingsUpdatedEvent>
    {
        private readonly IParsingService _parsingService;
        private readonly IHistoryService _historyService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private readonly ICached<TrackedDownload> _cache;

        public TrackedDownloadService(IParsingService parsingService,
            ICacheManager cacheManager,
            IHistoryService historyService,
            IConfigService configService,
            Logger logger)
        {
            _parsingService = parsingService;
            _historyService = historyService;
            _configService = configService;
            _cache = cacheManager.GetCache<TrackedDownload>(GetType());
            _logger = logger;
        }

        public TrackedDownload Find(string downloadId)
        {
            return _cache.Find(downloadId);
        }

        public TrackedDownload TrackDownload(DownloadClientDefinition downloadClient, DownloadClientItem downloadItem)
        {
            var existingItem = Find(downloadItem.DownloadId);

            if (existingItem != null)
            {
                return existingItem;
            }

            var trackedDownload = new TrackedDownload
            {
                TrackingId = downloadClient.Id + "-" + downloadItem.DownloadId,
                DownloadClient = downloadClient.Id,
                DownloadItem = downloadItem,
                Protocol = downloadClient.Protocol
            };

            var historyItem = _historyService.MostRecentForDownloadId(downloadItem.DownloadId);
            if (historyItem != null)
            {
                trackedDownload.State = GetStateFromHistory(historyItem.EventType);
            }

            try
            {
                var parsedEpisodeInfo = Parser.Parser.ParseTitle(trackedDownload.DownloadItem.Title);
                if (parsedEpisodeInfo == null) return null;

                var remoteEpisode = _parsingService.Map(parsedEpisodeInfo);
                if (remoteEpisode.Series == null)
                {
                    return null;
                }

                trackedDownload.RemoteEpisode = remoteEpisode;
            }
            catch (Exception e)
            {
                _logger.DebugException("Failed to find episode for " + downloadItem.Title, e);
                return null;
            }

            return trackedDownload;
        }

        public List<TrackedDownload> All()
        {
            return _cache.Values.ToList();
        }

        public List<TrackedDownload> GetActive()
        {
            var enabledFailedDownloadHandling = _configService.EnableFailedDownloadHandling;
            var enabledCompletedDownloadHandling = _configService.EnableCompletedDownloadHandling;

            var downloading = All()
                    .Where(v => v.State == TrackedDownloadStage.Downloading);

            if (!enabledCompletedDownloadHandling)
            {
                downloading = downloading.Where(c => c.DownloadItem.Status != DownloadItemStatus.Completed);
            }

            if (!enabledFailedDownloadHandling)
            {
                downloading = downloading.Where(c => c.DownloadItem.Status != DownloadItemStatus.Failed);
            }

            return downloading.ToList();
        }

        private static TrackedDownloadStage GetStateFromHistory(HistoryEventType eventType)
        {
            switch (eventType)
            {
                case HistoryEventType.SeriesFolderImported:
                    return TrackedDownloadStage.Imported;
                case HistoryEventType.DownloadFailed:
                    return TrackedDownloadStage.DownloadFailed;
                default:
                    return TrackedDownloadStage.Downloading;
            }
        }

        public void Handle(SceneMappingsUpdatedEvent message)
        {
            _cache.Clear();
        }
    }
}
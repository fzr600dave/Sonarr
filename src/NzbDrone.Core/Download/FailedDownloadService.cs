using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public interface IFailedDownloadService
    {
        void MarkAsFailed(int id);
        void Process(TrackedDownload trackedDownload);
    }

    public class FailedDownloadService : IFailedDownloadService
    {
        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public FailedDownloadService(IHistoryService historyService,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
        {
            _historyService = historyService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void MarkAsFailed(int id)
        {
            var history = _historyService.Get(id);

            var downloadClientId = history.DownloadId;
            if (downloadClientId.IsNullOrWhiteSpace())
            {
                PublishDownloadFailedEvent(new List<History.History> { history }, "Manually marked as failed");
            }
            else
            {
                var grabbedHistory = _historyService.Grabbed().Where(h => h.DownloadId == downloadClientId).ToList();
                PublishDownloadFailedEvent(grabbedHistory, "Manually marked as failed");
            }
        }

        public void Process(TrackedDownload trackedDownload)
        {
            var grabbedItems = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                .Where(c => c.EventType == HistoryEventType.Grabbed).ToList();

            if (grabbedItems.Empty())
            {
                trackedDownload.Warn("Download wasn't grabbed by sonarr, skipping");
                return;
            }

            if (trackedDownload.DownloadItem.IsEncrypted)
            {
                trackedDownload.State = TrackedDownloadStage.DownloadFailed;
                PublishDownloadFailedEvent(grabbedItems, "Encrypted download detected");
            }

            if (trackedDownload.DownloadItem.Status == DownloadItemStatus.Failed)
            {
                trackedDownload.State = TrackedDownloadStage.DownloadFailed;
                PublishDownloadFailedEvent(grabbedItems, trackedDownload.DownloadItem.Message);
            }
        }

        private void PublishDownloadFailedEvent(List<History.History> historyItems, string message)
        {
            var historyItem = historyItems.First();

            var downloadFailedEvent = new DownloadFailedEvent
            {
                SeriesId = historyItem.SeriesId,
                EpisodeIds = historyItems.Select(h => h.EpisodeId).ToList(),
                Quality = historyItem.Quality,
                SourceTitle = historyItem.SourceTitle,
                DownloadClient = historyItem.Data.GetValueOrDefault(History.History.DOWNLOAD_CLIENT),
                DownloadId = historyItem.DownloadId,
                Message = message,
                Data = historyItem.Data
            };

            _eventAggregator.PublishEvent(downloadFailedEvent);
        }


    }
}

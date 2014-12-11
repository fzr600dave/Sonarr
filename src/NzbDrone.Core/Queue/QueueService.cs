using System;
using System.Linq;
using System.Collections.Generic;
using NzbDrone.Core.Download.TrackedDownloads;

namespace NzbDrone.Core.Queue
{
    public interface IQueueService
    {
        List<Queue> GetQueue();
        Queue Find(int id);
    }

    public class QueueService : IQueueService
    {
        private readonly ITrackedDownloadService _trackedDownloadService;

        public QueueService(ITrackedDownloadService trackedDownloadService)
        {
            _trackedDownloadService = trackedDownloadService;
        }

        public List<Queue> GetQueue()
        {
            var queueItems = _trackedDownloadService.GetActive()
                .OrderBy(v => v.DownloadItem.RemainingTime)
                .ToList();

            return MapQueue(queueItems);
        }

        public Queue Find(int id)
        {
            return GetQueue().SingleOrDefault(q => q.Id == id);
        }

        private List<Queue> MapQueue(IEnumerable<TrackedDownload> trackedDownloads)
        {
            var queued = new List<Queue>();

            foreach (var trackedDownload in trackedDownloads)
            {
                foreach (var episode in trackedDownload.RemoteEpisode.Episodes)
                {
                    var queue = new Queue
                                {
                                    Id = episode.Id ^ (trackedDownload.DownloadItem.DownloadId.GetHashCode() << 16),
                                    Series = trackedDownload.RemoteEpisode.Series,
                                    Episode = episode,
                                    Quality = trackedDownload.RemoteEpisode.ParsedEpisodeInfo.Quality,
                                    Title = trackedDownload.DownloadItem.Title,
                                    Size = trackedDownload.DownloadItem.TotalSize,
                                    Sizeleft = trackedDownload.DownloadItem.RemainingSize,
                                    Timeleft = trackedDownload.DownloadItem.RemainingTime,
                                    Status = trackedDownload.DownloadItem.Status.ToString(),
                                    TrackedDownloadStatus = trackedDownload.Status.ToString(),
                                    StatusMessages = trackedDownload.StatusMessages.ToList(),
                                    RemoteEpisode = trackedDownload.RemoteEpisode,
                                    TrackingId = trackedDownload.TrackingId
                                };

                    if (queue.Timeleft.HasValue)
                    {
                        queue.EstimatedCompletionTime = DateTime.UtcNow.Add(queue.Timeleft.Value);
                    }

                    queued.Add(queue);
                }
            }

            return queued;
        }
    }
}

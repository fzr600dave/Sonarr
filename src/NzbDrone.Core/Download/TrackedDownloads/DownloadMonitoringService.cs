using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class DownloadMonitoringService : IExecute<CheckForFinishedDownloadCommand>,
                                             IHandleAsync<ApplicationStartedEvent>,
                                             IHandleAsync<EpisodeGrabbedEvent>
    {
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly IConfigService _configService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly Logger _logger;

        public DownloadMonitoringService(IProvideDownloadClient downloadClientProvider,
                                     IEventAggregator eventAggregator,
                                     IConfigService configService,
                                     ICacheManager cacheManager,
                                     IFailedDownloadService failedDownloadService,
                                     ICompletedDownloadService completedDownloadService,
                                     ITrackedDownloadService trackedDownloadService,
                                     Logger logger)
        {
            _downloadClientProvider = downloadClientProvider;
            _eventAggregator = eventAggregator;
            _configService = configService;
            _failedDownloadService = failedDownloadService;
            _completedDownloadService = completedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _logger = logger;

        }

        private void Refresh()
        {
            var downloadClients = _downloadClientProvider.GetDownloadClients();

            foreach (var downloadClient in downloadClients)
            {
                Refresh(downloadClient);
            }

            _eventAggregator.PublishEvent(new UpdateQueueEvent());
        }

        private void Refresh(IDownloadClient downloadClient)
        {
            List<DownloadClientItem> downloadClientHistory;
            try
            {
                downloadClientHistory = downloadClient.GetItems().ToList();
            }
            catch (Exception ex)
            {
                _logger.WarnException("Unable to retrieve queue and history items from " + downloadClient.Definition.Name, ex);
                return;
            }

            foreach (var downloadItem in downloadClientHistory)
            {
                var trackedDownload = _trackedDownloadService.TrackDownload((DownloadClientDefinition)downloadClient.Definition, downloadItem);
                if (trackedDownload != null && trackedDownload.State == TrackedDownloadStage.Downloading)
                {
                    ProcessTrackedDownload(trackedDownload);
                }
            }
        }

        private void ProcessTrackedDownload(TrackedDownload trackedDownload)
        {
            _failedDownloadService.Process(trackedDownload);

            if (_configService.EnableCompletedDownloadHandling)
            {
                _completedDownloadService.Process(trackedDownload);
            }
        }


        public void Execute(CheckForFinishedDownloadCommand message)
        {
            Refresh();
        }

        public void HandleAsync(ApplicationStartedEvent message)
        {
            Refresh();
        }

        public void HandleAsync(EpisodeGrabbedEvent message)
        {
            Refresh();
        }
    }
}

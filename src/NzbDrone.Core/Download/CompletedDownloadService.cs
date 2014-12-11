﻿using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using System.IO;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public interface ICompletedDownloadService
    {
        void Process(TrackedDownload trackedDownload);
    }

    public class CompletedDownloadService : ICompletedDownloadService
    {
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IHistoryService _historyService;
        private readonly IDownloadedEpisodesImportService _downloadedEpisodesImportService;
        private readonly Logger _logger;

        public CompletedDownloadService(IConfigService configService,
                                        IEventAggregator eventAggregator,
                                        IHistoryService historyService,
                                        IDownloadedEpisodesImportService downloadedEpisodesImportService,
                                        Logger logger)
        {
            _configService = configService;
            _eventAggregator = eventAggregator;
            _historyService = historyService;
            _downloadedEpisodesImportService = downloadedEpisodesImportService;
            _logger = logger;
        }

        public void Process(TrackedDownload trackedDownload)
        {
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Completed)
            {
                return;
            }

            var historyItem = _historyService.MostRecentForDownloadId(trackedDownload.DownloadItem.DownloadId);

            if (historyItem == null && trackedDownload.DownloadItem.Category.IsNullOrWhiteSpace())
            {
                trackedDownload.Warn("Download wasn't grabbed by Sonarr and not in a category, Skipping.");
                return;
            }

            var downloadItemOutputPath = trackedDownload.DownloadItem.OutputPath;

            if (downloadItemOutputPath.IsEmpty)
            {
                trackedDownload.Warn("Download doesn't contain intermediate path, Skipping.");
                return;
            }

            var downloadedEpisodesFolder = new OsPath(_configService.DownloadedEpisodesFolder);
            if (downloadedEpisodesFolder.Contains(downloadItemOutputPath))
            {
                trackedDownload.Warn("Intermediate Download path inside drone factory, Skipping.");
                return;
            }

            Import(trackedDownload);
        }



        private void Import(TrackedDownload trackedDownload)
        {
            var outputPath = trackedDownload.DownloadItem.OutputPath.FullPath;
            var importResults = _downloadedEpisodesImportService.ProcessPath(outputPath, trackedDownload.DownloadItem);

            if (importResults.Empty())
            {
                trackedDownload.Warn("No files found are eligible for import in {0}", outputPath);
                return;
            }

            if (importResults.Any(c => c.Result != ImportResultType.Imported))
            {
                var statusMessages = importResults
                    .Where(v => v.Result != ImportResultType.Imported)
                    .Select(v => new TrackedDownloadStatusMessage(Path.GetFileName(v.ImportDecision.LocalEpisode.Path), v.Errors))
                    .ToArray();

                trackedDownload.Warn(statusMessages);
                return;
            }

            trackedDownload.State = TrackedDownloadStage.Imported;
            _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload));

        }
    }
}

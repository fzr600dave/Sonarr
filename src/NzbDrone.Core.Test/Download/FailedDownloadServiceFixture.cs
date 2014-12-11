using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class FailedDownloadServiceFixture : CoreTest<DownloadMonitoringService>
    {
        private List<DownloadClientItem> _completed;
        private List<DownloadClientItem> _failed;

        [SetUp]
        public void Setup()
        {
            _completed = Builder<DownloadClientItem>.CreateListOfSize(5)
                                                    .All()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.IsEncrypted = false)
                                                    .With(h => h.Title = "Drone.S01E01.HDTV")
                                                    .Build()
                                                    .ToList();

            _failed = Builder<DownloadClientItem>.CreateListOfSize(1)
                                                 .All()
                                                 .With(h => h.Status = DownloadItemStatus.Failed)
                                                 .With(h => h.Title = "Drone.S01E01.HDTV")
                                                 .Build()
                                                 .ToList();

            var remoteEpisode = new RemoteEpisode
            {
                Series = new Series(),
                Episodes = new List<Episode> { new Episode { Id = 1 } }
            };

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(c => c.GetDownloadClients())
                  .Returns(new IDownloadClient[] { Mocker.GetMock<IDownloadClient>().Object });

            Mocker.GetMock<IDownloadClient>()
                  .SetupGet(c => c.Definition)
                  .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient" });

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableFailedDownloadHandling)
                  .Returns(true);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Imported())
                  .Returns(new List<History.History>());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<Int32>(), It.IsAny<IEnumerable<Int32>>()))
                  .Returns(remoteEpisode);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedEpisodeInfo>(), It.IsAny<Int32>(), (SearchCriteriaBase)null))
                  .Returns(remoteEpisode);

            Mocker.SetConstant<IFailedDownloadService>(Mocker.Resolve<FailedDownloadService>());
        }

        private void GivenNoGrabbedHistory()
        {
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Grabbed())
                  .Returns(new List<History.History>());
        }

        private void GivenGrabbedHistory(List<History.History> history)
        {
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Grabbed())
                  .Returns(history);
        }

        private void GivenNoFailedHistory()
        {
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Failed())
                  .Returns(new List<History.History>());
        }

        private void GivenFailedHistory(List<History.History> failedHistory)
        {
            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Failed())
                  .Returns(failedHistory);
        }

        private void GivenFailedDownloadClientHistory()
        {
            Mocker.GetMock<IDownloadClient>()
                  .Setup(s => s.GetItems())
                  .Returns(_failed);
        }


        private void VerifyNoFailedDownloads()
        {
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }

        private void VerifyFailedDownloads(int count = 1)
        {
            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<DownloadFailedEvent>(d => d.EpisodeIds.Count == count)), Times.Once());
        }

        private void VerifyRetryDownload()
        {
            Mocker.GetMock<IDownloadClient>()
                .Verify(v => v.RetryDownload(It.IsAny<String>()), Times.Once());
        }

        private void VerifyNoRetryDownload()
        {
            Mocker.GetMock<IDownloadClient>()
                .Verify(v => v.RetryDownload(It.IsAny<String>()), Times.Never());
        }

        [Test]
        public void should_not_process_if_no_download_client_history()
        {
            Mocker.GetMock<IDownloadClient>()
                  .Setup(s => s.GetItems())
                  .Returns(new List<DownloadClientItem>());

            Subject.Execute(new CheckForFinishedDownloadCommand());

            Mocker.GetMock<IHistoryService>()
                  .Verify(s => s.BetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), HistoryEventType.Grabbed),
                      Times.Never());

            VerifyNoFailedDownloads();
        }

        [Test]
        public void should_not_process_if_no_failed_items_in_download_client_history()
        {
            GivenNoGrabbedHistory();
            GivenNoFailedHistory();

            Mocker.GetMock<IDownloadClient>()
                  .Setup(s => s.GetItems())
                  .Returns(_completed);

            Subject.Execute(new CheckForFinishedDownloadCommand());

            Mocker.GetMock<IHistoryService>()
                  .Verify(s => s.BetweenDates(It.IsAny<DateTime>(), It.IsAny<DateTime>(), HistoryEventType.Grabbed),
                      Times.Never());

            VerifyNoFailedDownloads();
        }

        [Test]
        public void should_not_process_if_matching_history_is_not_found()
        {
            GivenNoGrabbedHistory();
            GivenFailedDownloadClientHistory();

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyNoFailedDownloads();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_process_if_grabbed_history_contains_null_downloadclient_id()
        {
            GivenFailedDownloadClientHistory();

            var historyGrabbed = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            historyGrabbed.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            historyGrabbed.First().DownloadId = null;

            GivenGrabbedHistory(historyGrabbed);
            GivenNoFailedHistory();

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyNoFailedDownloads();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_process_if_failed_history_contains_null_downloadclient_id()
        {
            GivenFailedDownloadClientHistory();

            var historyGrabbed = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            historyGrabbed.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            historyGrabbed.First().DownloadId = _failed.First().DownloadId;

            GivenGrabbedHistory(historyGrabbed);

            var historyFailed = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            historyFailed.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            historyFailed.First().DownloadId = null;

            GivenFailedHistory(historyFailed);

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyFailedDownloads();
        }

        [Test]
        public void should_not_process_if_already_added_to_history_as_failed()
        {
            GivenFailedDownloadClientHistory();

            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenFailedHistory(history);

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _failed.First().DownloadId;

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyNoFailedDownloads();
        }

        [Test]
        public void should_process_if_not_already_in_failed_history()
        {
            GivenFailedDownloadClientHistory();

            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoFailedHistory();

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _failed.First().DownloadId;

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyFailedDownloads();
        }

        [Test]
        public void should_have_multiple_episode_ids_when_multi_episode_release_fails()
        {
            GivenFailedDownloadClientHistory();

            var history = Builder<History.History>.CreateListOfSize(2)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoFailedHistory();

            history.ForEach(h =>
            {
                h.Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
                h.DownloadId = _failed.First().DownloadId;
            });

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyFailedDownloads(2);
        }

        [Test]
        public void should_skip_if_enable_failed_download_handling_is_off()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableFailedDownloadHandling)
                  .Returns(false);

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyNoFailedDownloads();
        }

        [Test]
        public void should_process_if_ageHours_is_not_set()
        {
            GivenFailedDownloadClientHistory();

            var historyGrabbed = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            historyGrabbed.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            historyGrabbed.First().DownloadId = _failed.First().DownloadId;

            GivenGrabbedHistory(historyGrabbed);
            GivenNoFailedHistory();

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyFailedDownloads();
            VerifyNoRetryDownload();
        }

        [Test]
        public void should_process_if_age_is_greater_than_grace_period()
        {
            GivenFailedDownloadClientHistory();

            var historyGrabbed = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            historyGrabbed.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            historyGrabbed.First().DownloadId = _failed.First().DownloadId;
            historyGrabbed.First().Data.Add("ageHours", "48");

            GivenGrabbedHistory(historyGrabbed);
            GivenNoFailedHistory();

            Subject.Execute(new CheckForFinishedDownloadCommand());

            VerifyFailedDownloads();
            VerifyNoRetryDownload();
        }

        [Test]
        public void should_manual_mark_all_episodes_of_release_as_failed()
        {
            var historyFailed = Builder<History.History>.CreateListOfSize(2)
                .All()
                .With(v => v.EventType == HistoryEventType.Grabbed)
                .Do(v => v.Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient"))
                .Do(v => v.DownloadId = "test")
                .Build()
                .ToList();

            GivenGrabbedHistory(historyFailed);

            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.Get(It.IsAny<Int32>()))
                .Returns<Int32>(i => historyFailed.FirstOrDefault(v => v.Id == i));

            //Subject.MarkAsFailed(1);

            VerifyFailedDownloads(2);
        }
    }
}

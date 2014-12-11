using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class CompletedDownloadServiceFixture : CoreTest<CompletedDownloadService>
    {
        private DownloadClientItem _completed;
        private List<History.History> _grabbedHistory;
        private List<History.History> _importedHistory;
        private TrackedDownload _trackedDownload;


        [SetUp]
        public void Setup()
        {
            _grabbedHistory = null;
            _importedHistory = null;

            _completed = Builder<DownloadClientItem>.CreateNew()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                                    .With(h => h.Title = "Drone.S01E01.HDTV")
                                                    .Build();


            var remoteEpisode = new RemoteEpisode
                                {
                                    Series = new Series(),
                                    Episodes = new List<Episode> { new Episode { Id = 1 } }
                                };


            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                    .With(c => c.State = TrackedDownloadStage.Downloading)
                    .With(c => c.DownloadItem = _completed)
                    .With(c => c.RemoteEpisode = remoteEpisode)
                    .Build();


            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient" });

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(c => c.Get(It.IsAny<int>()))
                  .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Failed())
                  .Returns(new List<History.History>());

        }

        private void GivenNoGrabbedHistory()
        {
            GivenGrabbedHistory(new List<History.History>());
        }

        private void GivenGrabbedHistory(List<History.History> history)
        {
            _grabbedHistory = history;


            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Grabbed())
                  .Returns(history);
        }

        private void GivenNoImportedHistory()
        {
            GivenImportedHistory(new List<History.History>());
        }

        private void GivenImportedHistory(List<History.History> importedHistory)
        {
            _importedHistory = importedHistory;


            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.Imported())
                  .Returns(importedHistory);
        }

        private void GivenRemoveConfig(bool remove)
        {
            Mocker.GetMock<IConfigService>()
             .SetupGet(s => s.RemoveCompletedDownloads)
             .Returns(remove);
        }

        private void GivenSuccessfulImport()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>
                    {
                        new ImportResult(new ImportDecision(new LocalEpisode() { Path = @"C:\TestPath\Droned.S01E01.mkv" }))
                    });
        }

        private void GivenFailedImport()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>() 
                    {
                        new ImportResult(new ImportDecision(new LocalEpisode() { Path = @"C:\TestPath\Droned.S01E01.mkv" }, "Test Failure")) 
                    });
        }
     

        [Test]
        public void should_not_process_if_matching_history_is_not_found_and_no_category_specified()
        {
            _completed.Category = null;

            GivenNoGrabbedHistory();
            GivenNoImportedHistory();



            Subject.Process(_trackedDownload);

            AssertNoImports();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_process_if_matching_history_is_not_found_but_category_specified()
        {
            _completed.Category = "tv";

            GivenNoGrabbedHistory();
            GivenNoImportedHistory();
            GivenSuccessfulImport();

            Subject.Process(_trackedDownload);

            AssertImports();
        }


        [Test]
        public void should_not_process_if_already_added_to_history_as_imported()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenImportedHistory(history);

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertNoImports();
        }

        [Test]
        public void should_process_if_not_already_in_imported_history()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();
            GivenSuccessfulImport();

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertImports();
        }

        [Test]
        public void should_not_process_if_storage_directory_does_not_exist()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();
            GivenSuccessfulImport();

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertNoImports();

            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_not_process_if_storage_directory_in_drone_factory()
        {

            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();
            GivenSuccessfulImport();

            Mocker.GetMock<IConfigService>()
                  .SetupGet(v => v.DownloadedEpisodesFolder)
                  .Returns(@"C:\DropFolder".AsOsAgnostic());

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertNoImports();

            ExceptionVerification.IgnoreWarns();
        }


        [Test]
        public void should_not_process_if_output_path_is_empty()
        {

            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();
            GivenSuccessfulImport();

            _trackedDownload.DownloadItem.OutputPath = new OsPath();

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertNoImports();

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public void should_process_as_already_imported_if_drone_factory_import_history_exists()
        {
            var completed = new List<DownloadClientItem>();
            completed.AddRange(Builder<DownloadClientItem>.CreateListOfSize(2)
                                             .All()
                                             .With(h => h.Status = DownloadItemStatus.Completed)
                                             .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                             .With(h => h.Title = "Drone.S01E01.HDTV")
                                             .Build());

            var grabbedHistory = Builder<History.History>.CreateListOfSize(2)
                                                  .All()
                                                  .With(d => d.Data[History.History.DOWNLOAD_CLIENT] = "SabnzbdClient")
                                                  .TheFirst(1)
                                                  .With(d => d.DownloadId = _completed.DownloadId)
                                                  .With(d => d.SourceTitle = "Droned.S01E01.720p-LAZY")
                                                  .TheLast(1)
                                                  .With(d => d.DownloadId = completed.Last().DownloadId)
                                                  .With(d => d.SourceTitle = "Droned.S01E01.Proper.720p-LAZY")
                                                  .Build()
                                                  .ToList();

            var importedHistory = Builder<History.History>.CreateListOfSize(2)
                                                  .All()
                                                  .With(d => d.EpisodeId = 1)
                                                  .TheFirst(1)
                                                  .With(d => d.Data["droppedPath"] = @"C:\mydownload\Droned.S01E01.720p-LAZY\lzy-dr101.mkv".AsOsAgnostic())
                                                  .TheLast(1)
                                                  .With(d => d.Data["droppedPath"] = @"C:\mydownload\Droned.S01E01.Proper.720p-LAZY\lzy-dr101.mkv".AsOsAgnostic())
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(grabbedHistory);
            GivenImportedHistory(importedHistory);

            Subject.Process(_trackedDownload);

            AssertNoImports();

            Mocker.GetMock<IHistoryService>()
                .Verify(v => v.UpdateHistory(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Dictionary<String, String>>()), Times.Once());
        }

        [Test]
        public void should_not_remove_while_readonly()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();
            GivenGrabbedHistory(history);
            GivenNoImportedHistory();
            GivenSuccessfulImport();
            GivenRemoveConfig(true);

            _completed.IsReadOnly = true;

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertNotRemoved();
        }


        [Test]
        public void should_not_remove_if_imported_failed()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            Mocker.GetMock<IDownloadedEpisodesImportService>()
          .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
          .Returns(new List<ImportResult>
                           {
                               new ImportResult(
                                   new ImportDecision(new LocalEpisode() {Path = @"C:\TestPath\Droned.S01E01.mkv"}, "Rejected!"),
                                   "Test Failure")
                           });

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();
            GivenFailedImport();
            GivenRemoveConfig(true);

            _completed.IsReadOnly = false;

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertNotRemoved();

            ExceptionVerification.IgnoreErrors();
        }

        [Test]
        public void should_remove_if_imported()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();
            GivenSuccessfulImport();
            GivenRemoveConfig(true);

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertRemoved();
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_rejected()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();

            Mocker.GetMock<IDownloadedEpisodesImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(
                                   new ImportDecision(new LocalEpisode() {Path = @"C:\TestPath\Droned.S01E01.mkv"}, "Rejected!"),
                                   "Test Failure")
                           });

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);


            AssertNoImports();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_skipped()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();

            Mocker.GetMock<IDownloadedEpisodesImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(
                                   new ImportDecision(new LocalEpisode() {Path = @"C:\TestPath\Droned.S01E01.mkv"}),
                                   "Test Failure")
                           });

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);


            AssertNoImports();

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_mark_as_imported_if_some_files_were_skipped()
        {
            var history = Builder<History.History>.CreateListOfSize(1)
                                                  .Build()
                                                  .ToList();

            GivenGrabbedHistory(history);
            GivenNoImportedHistory();

            Mocker.GetMock<IDownloadedEpisodesImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision(new LocalEpisode() {Path = @"C:\TestPath\Droned.S01E01.mkv"})),
                               new ImportResult(
                                   new ImportDecision(new LocalEpisode() {Path = @"C:\TestPath\Droned.S01E01.mkv"}),
                                   "Test Failure")
                           });

            history.First().Data.Add(History.History.DOWNLOAD_CLIENT, "SabnzbdClient");
            history.First().DownloadId = _completed.DownloadId;

            Subject.Process(_trackedDownload);

            AssertNoImports();

            ExceptionVerification.ExpectedErrors(1);
        }


        private void AssertNotRemoved()
        {
            Mocker.GetMock<IProvideDownloadClient>().Verify(c => c.Get(It.IsAny<int>()), Times.Never());
        }


        private void AssertRemoved()
        {
            Mocker.GetMock<IProvideDownloadClient>().Verify(c => c.Get(It.IsAny<int>()), Times.Once());
            Mocker.GetMock<IDownloadClient>().Verify(c => c.RemoveItem(_trackedDownload.DownloadItem.DownloadId), Times.Once());
        }


        private void AssertNoImports()
        {
            Mocker.GetMock<IDownloadedEpisodesImportService>()
                .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        private void AssertImports()
        {
            _trackedDownload.State.Should().Be(TrackedDownloadStage.Imported);

            Mocker.GetMock<IDownloadedEpisodesImportService>()
                .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<DownloadClientItem>()), Times.Once());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Test.TvTests.SeriesAddedServiceTests
{
    [TestFixture]
    public class SetEpisodeMontitoredFixture : CoreTest<SeriesAddedService>
    {
        private Series _series;
        private List<Episode> _episodes;

        [SetUp]
        public void Setup()
        {
            var seasons = 4;

            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Seasons = Builder<Season>.CreateListOfSize(seasons).All().With(n => n.Monitored = true).Build().ToList())
                                     .Build();

            _episodes = Builder<Episode>.CreateListOfSize(seasons)
                                        .All()
                                        .With(e => e.Monitored = true)
                                        .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(-7))
                                        //Missing
                                        .TheFirst(1)
                                        .With(e => e.EpisodeFileId = 0)
                                        //Has File
                                        .TheNext(1)
                                        .With(e => e.EpisodeFileId = 1)
                                         //Future
                                        .TheNext(1)
                                        .With(e => e.EpisodeFileId = 0)
                                        .With(e => e.AirDateUtc = DateTime.UtcNow.AddDays(7))
                                        //Future/TBA
                                        .TheNext(1)
                                        .With(e => e.EpisodeFileId = 0)
                                        .With(e => e.AirDateUtc = null)
                                        //TODO: Need cases for specials (they should always be ignored)
                                        .Build()
                                        .ToList();

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodeBySeries(It.IsAny<int>()))
                  .Returns(_episodes);
        }

        private void WithSeriesAddedEvent(AddSeriesOptions options)
        {
            Subject.Handle(new SeriesAddedEvent(_series, options));
        }

        private void WithSeriesScannedEvent()
        {
            Subject.Handle(new SeriesScannedEvent(_series));
        }

        private void GivenSpecials()
        {
            foreach (var episode in _episodes)
            {
                episode.SeasonNumber = 0;
            }

            _series.Seasons = new List<Season>{new Season { Monitored = false, SeasonNumber = 0 }};
        }

        [Test]
        public void should_be_able_to_monitor_all_episodes()
        {
            WithSeriesAddedEvent(new AddSeriesOptions());
            WithSeriesScannedEvent();

            Mocker.GetMock<IEpisodeService>()
                  .Verify(v => v.UpdateEpisodes(It.Is<List<Episode>>(l => l.All(e => e.Monitored))));
        }

        [Test]
        public void should_be_able_to_monitor_missing_episodes_only()
        {
            WithSeriesAddedEvent(new AddSeriesOptions
                                 {
                                     IgnoreEpisodesWithFiles = true,
                                     IgnoreEpisodesWithoutFiles = false
                                 });

            WithSeriesScannedEvent();

            VerifyMonitored(e => !e.HasFile);
            VerifyNotMonitored(e => e.HasFile);
        }

        [Test]
        public void should_be_able_to_monitor_new_episodes_only()
        {
            WithSeriesAddedEvent(new AddSeriesOptions
            {
                IgnoreEpisodesWithFiles = true,
                IgnoreEpisodesWithoutFiles = true
            });

            WithSeriesScannedEvent();

            VerifyMonitored(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value.After(DateTime.UtcNow));
            VerifyMonitored(e => !e.AirDateUtc.HasValue);
            VerifyNotMonitored(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value.Before(DateTime.UtcNow));
        }

        [Test]
        public void should_be_able_to_monitor_first_season_only()
        {
            WithSeriesAddedEvent(new AddSeriesOptions
            {
                IgnoreSeasons = _series.Seasons.Select(s => s.SeasonNumber).Except(new []{1}).ToList()
            });

            WithSeriesScannedEvent();

            Mocker.GetMock<ISeriesService>()
                  .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Seasons.Single(n => n.SeasonNumber == 1).Monitored)));

            Mocker.GetMock<ISeriesService>()
                  .Verify(v => v.UpdateSeries(It.Is<Series>(s => s.Seasons.Where(n => n.SeasonNumber != 1).All(n => !n.Monitored))));
        }

        [Test]
        public void should_not_monitor_missing_speicals()
        {
            GivenSpecials();

            WithSeriesAddedEvent(new AddSeriesOptions
            {
                IgnoreEpisodesWithFiles = true,
                IgnoreEpisodesWithoutFiles = false
            });

            WithSeriesScannedEvent();

            VerifyMonitored(e => !e.HasFile);
            VerifyNotMonitored(e => e.HasFile);
        }

        [Test]
        public void should_not_monitor_new_specials()
        {
            GivenSpecials();

            WithSeriesAddedEvent(new AddSeriesOptions
            {
                IgnoreEpisodesWithFiles = true,
                IgnoreEpisodesWithoutFiles = true
            });

            WithSeriesScannedEvent();
            VerifyMonitored(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value.After(DateTime.UtcNow));
            VerifyMonitored(e => !e.AirDateUtc.HasValue);
            VerifyNotMonitored(e => e.AirDateUtc.HasValue && e.AirDateUtc.Value.Before(DateTime.UtcNow));
        }

        private void VerifyMonitored(Func<Episode, bool> predicate)
        {
            Mocker.GetMock<IEpisodeService>()
                .Verify(v => v.UpdateEpisodes(It.Is<List<Episode>>(l => l.Where(predicate).All(e => e.Monitored))));
        }

        private void VerifyNotMonitored(Func<Episode, bool> predicate)
        {
            Mocker.GetMock<IEpisodeService>()
                .Verify(v => v.UpdateEpisodes(It.Is<List<Episode>>(l => l.Where(predicate).All(e => !e.Monitored))));
        }
    }
}

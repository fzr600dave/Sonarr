using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class NotInQueueSpecification : IDecisionEngineSpecification
    {
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly Logger _logger;

        public NotInQueueSpecification(ITrackedDownloadService trackedDownloadService, Logger logger)
        {
            _trackedDownloadService = trackedDownloadService;
            _logger = logger;
        }

        public RejectionType Type { get { return RejectionType.Permanent; } }

        public Decision IsSatisfiedBy(RemoteEpisode subject, SearchCriteriaBase searchCriteria)
        {
            var queue = _trackedDownloadService.GetActive()
                            .Where(v => v.State == TrackedDownloadStage.Downloading)
                            .Select(q => q.RemoteEpisode).ToList();

            if (IsInQueue(subject, queue))
            {
                _logger.Debug("Already in queue, rejecting.");
                return Decision.Reject("Already in download queue");
            }

            return Decision.Accept();
        }

        private bool IsInQueue(RemoteEpisode newEpisode, IEnumerable<RemoteEpisode> episodesInQueue)
        {
            var matchingSeries = episodesInQueue.Where(q => q.Series.Id == newEpisode.Series.Id);
            var matchingSeriesAndQuality = matchingSeries.Where(q => new QualityModelComparer(q.Series.Profile).Compare(q.ParsedEpisodeInfo.Quality, newEpisode.ParsedEpisodeInfo.Quality) >= 0);

            return matchingSeriesAndQuality.Any(q => q.Episodes.Select(e => e.Id).Intersect(newEpisode.Episodes.Select(e => e.Id)).Any());
        }
    }
}

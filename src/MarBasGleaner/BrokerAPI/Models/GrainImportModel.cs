using CraftedSolutions.MarBasSchema.Transport;
using System.Collections.Immutable;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Models
{
    internal sealed class GrainImportModel
    {
        public ISet<IGrainTransportable> Grains { get; set; } = ImmutableHashSet<IGrainTransportable>.Empty;
        public ISet<Guid>? GrainsToDelete { get; set; }
        public DuplicatesHandlingStrategy? DuplicatesHandling { get; set; }
    }
}

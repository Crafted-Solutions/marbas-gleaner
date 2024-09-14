using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace MarBasGleaner.Tracking
{
    internal class Snapshot
    {
        public Version? SchemaVersion { get; set; }
        public Guid InstanceId { get; set; } = Guid.Empty;
        public IEnumerable<Guid> Anchor { get; set; } = Array.Empty<Guid>();
        [JsonIgnore]
        [IgnoreDataMember]
        public Guid AnchorId => Anchor.LastOrDefault();
        public SnapshotScope Scope { get; set; } = SnapshotScope.Recursive;
        public DateTime Latest { get; set; }
        public DateTime Updated { get; set; }
        public ISet<Guid> DeadGrains { get; set; } = new HashSet<Guid>();
        public ISet<Guid> AliveGrains { get; set; } = new HashSet<Guid>();
    }
}

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
        public DateTime Updated { get; set; }
        public int Checkpoint { get; set; }
    }
}

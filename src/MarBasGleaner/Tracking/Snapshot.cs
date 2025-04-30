using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace CraftedSolutions.MarBasGleaner.Tracking
{
    internal class Snapshot
    {
        public static readonly Version SupportedVersion = new(0, 1, 0);

        public Version Version { get; set; } = SupportedVersion;
        public Version? SchemaVersion { get; set; }
        public IEnumerable<Guid> Anchor { get; set; } = Array.Empty<Guid>();
        [JsonIgnore]
        [IgnoreDataMember]
        public Guid AnchorId => Anchor.FirstOrDefault();
        public SnapshotScope Scope { get; set; } = SnapshotScope.Recursive;
        public DateTime Updated { get; set; }
        public int Checkpoint { get; set; }
    }
}

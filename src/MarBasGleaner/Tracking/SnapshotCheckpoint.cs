namespace CraftedSolutions.MarBasGleaner.Tracking
{
    internal class SnapshotCheckpoint : ICloneable
    {
        public static readonly DateTime BuiltInGrainsMTime = new(2024, 1, 5, 0, 0, 11, DateTimeKind.Utc);

        public int Ordinal { get; set; } = 0;
        public Guid InstanceId { get; set; } = Guid.Empty;
        public DateTime Latest { get; set; } = BuiltInGrainsMTime;
        public ISet<Guid> Deletions { get; set; } = new HashSet<Guid>();
        public ISet<Guid> Modifications { get; set; } = new HashSet<Guid>();

        public SnapshotCheckpoint Clone(bool deep = false)
        {
            var result = (SnapshotCheckpoint)MemberwiseClone();
            if (deep)
            {
                result.Modifications = new HashSet<Guid>(Modifications);
                result.Deletions = new HashSet<Guid>(Deletions);
            }
            return result;
        }

        public bool IsSame(SnapshotCheckpoint? other)
        {
            return ReferenceEquals(this, other) || null != other && InstanceId == other.InstanceId && Ordinal == other.Ordinal && Latest == other.Latest;
        }

        object ICloneable.Clone()
        {
            return Clone();
        }
    }
}

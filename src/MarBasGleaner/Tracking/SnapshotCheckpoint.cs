namespace MarBasGleaner.Tracking
{
    internal class SnapshotCheckpoint: ICloneable
    {
        public int Ordinal { get; set; } = 0;
        public Guid InstanceId { get; set; } = Guid.Empty;
        public DateTime Latest { get; set; } = DateTime.MinValue.ToUniversalTime();
        public ISet<Guid> Deletions { get; set; } = new HashSet<Guid>();
        public ISet<Guid> Additions { get; set; } = new HashSet<Guid>();

        public SnapshotCheckpoint Clone(bool deep = false)
        {
            var result = (SnapshotCheckpoint)MemberwiseClone();
            if (deep)
            {
                result.Additions = new HashSet<Guid>(Additions);
                result.Deletions = new HashSet<Guid>(Deletions);
            }
            return result;
        }

        public bool IsSame(SnapshotCheckpoint? other)
        {
            return ReferenceEquals(this, other) || (null != other && InstanceId == other.InstanceId && Ordinal == other.Ordinal && Latest == other.Latest);
        }

        object ICloneable.Clone()
        {
            return Clone();
        }
    }
}

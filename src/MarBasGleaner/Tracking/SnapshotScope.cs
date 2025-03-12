namespace CraftedSolutions.MarBasGleaner.Tracking
{
    public enum SnapshotScope
    {
        Anchor = 0x01,
        Children = 0x10,
        Descendants = 0x20,
        Family = Anchor | Children,
        Recursive = Anchor | Descendants
    }
}

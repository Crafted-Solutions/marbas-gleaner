namespace MarBasGleaner.Commands
{
    internal enum CmdResultCode
    {
        Success = 0,
        ParameterError = -2,
        SnapshotStateError = -1,
        SnapshotVersionError = 1,
        BrokerConnectionError = 2,
        SchemaVersionError = 3,
        APIVersionError = 4,
        AnchorGrainError = 5,
        SnapshotInitError = 6,
        GrainLoadError = 7,
        BrokerPushError = 8,
        SnapshotStatusOutofdate = 42
    }
}

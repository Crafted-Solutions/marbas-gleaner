namespace MarBasGleaner.Commands
{
    internal enum CmdResultCode
    {
        Success = 0,
        ParameterError = -2,
        SnapshotStateError = -1,
        SnapshotVersionError = 1,
        BrokerConnectionError,
        SchemaVersionError,
        APIVersionError,
        InstanceIdError,
        AnchorGrainError,
        SnapshotInitError,
        GrainLoadError,
        BrokerPushError,
        SnapshotStatusOutofdate = 42
    }
}

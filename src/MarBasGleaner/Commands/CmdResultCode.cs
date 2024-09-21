namespace MarBasGleaner.Commands
{
    internal enum CmdResultCode
    {
        Success = 0,
        ParameterError = -2,
        SnapshotStateError = -1,
        BrokerConnectionError = 1,
        SchemaVersionError = 2,
        AnchorGrainError = 3,
        SnapshotInitError = 4,
        SnapshotStatusOutofdate = 42
    }
}

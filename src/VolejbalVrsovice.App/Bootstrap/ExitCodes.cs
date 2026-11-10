namespace VolejbalVrsovice.App.Bootstrap
{
    /// <summary>Návratové kódy procesu – usnadňují dohledání, proč se aplikace nespustila.</summary>
    public static class ExitCodes
    {
        public const int Ok = 0;
        public const int UnexpectedError = 1;
        public const int FrameworkMissing = 2;
        public const int DataFolderNotWritable = 3;
        public const int AlreadyRunning = 4;
        public const int DataStoreInconsistent = 5;
    }
}

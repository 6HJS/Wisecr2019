namespace SecuCodeApp
{
    public interface IProgressListener
    {
        void Error(string error, ushort? tag);
        
        void Update(ushort tag, Stage stage, double stageProgress, string description = null);
        void Update(ushort tag, double stageProgress, string description = null);

        void Info(string v);
        void SetPilotTag(ushort v);
    }

    public enum Stage
    {
        Waiting,
        GoToBootloader,
        SendSessionKey,
        WaitingForDataObserver,
        SendingData,
        Updated,
        Attesting,
        SendingChallenge,
        ReadingResponse,
        Done,
        Skipped,
    }
}
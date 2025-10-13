namespace GSADUs.Revit.Addin
{
    public enum StagingDecision
    {
        ResolveElements,
        ResolveCategories,
        Continue,
        Cancel
    }

    public interface IDialogService
    {
        void Info(string title, string message);
        bool ConfirmYesNo(string title, string mainInstruction, string? content = null, bool defaultYes = true);
        StagingDecision StagingPrompt(string title, string mainInstruction, string? content = null, StagingDecision defaultChoice = StagingDecision.ResolveElements);
    }
}

using Autodesk.Revit.UI;

namespace GSADUs.Revit.Addin
{
    internal sealed class DialogService : IDialogService
    {
        public void Info(string title, string message)
        {
            try { TaskDialog.Show(title, message); } catch { }
        }

        public bool ConfirmYesNo(string title, string mainInstruction, string? content = null, bool defaultYes = true)
        {
            try
            {
                var td = new TaskDialog(title)
                {
                    MainInstruction = mainInstruction,
                    MainContent = content ?? string.Empty,
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = defaultYes ? TaskDialogResult.Yes : TaskDialogResult.No
                };
                return td.Show() == TaskDialogResult.Yes;
            }
            catch { return defaultYes; }
        }

        public StagingDecision StagingPrompt(string title, string mainInstruction, string? content = null, StagingDecision defaultChoice = StagingDecision.ResolveElements)
        {
            try
            {
                var td = new TaskDialog(title)
                {
                    MainInstruction = mainInstruction,
                    MainContent = content ?? string.Empty,
                    AllowCancellation = true
                };

                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Resolve by Elements");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Resolve by Categories");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Continue");
                td.CommonButtons = TaskDialogCommonButtons.Cancel;

                var r = td.Show();
                if (r == TaskDialogResult.CommandLink1) return StagingDecision.ResolveElements;
                if (r == TaskDialogResult.CommandLink2) return StagingDecision.ResolveCategories;
                if (r == TaskDialogResult.CommandLink3) return StagingDecision.Continue;
                return StagingDecision.Cancel;
            }
            catch
            {
                return defaultChoice;
            }
        }
    }
}

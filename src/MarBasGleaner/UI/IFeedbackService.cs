using System.Security;

namespace CraftedSolutions.MarBasGleaner.UI
{
    [Flags]
    public enum MessageSeparatorOption
    {
        None = 0x0, Before = 0x1, After = 0x2, Both = Before | After
    }

    public interface IFeedbackService
    {
        void DisplayError(string message);
        void DisplayWarning(string message);
        void DisplayInfo(string message);
        void DisplayMessage(string message, MessageSeparatorOption separatorOption = MessageSeparatorOption.None);
        string? GetText(string prompt, string? defaultValue = null);
        SecureString GetSecureText(string prompt);
    }

}
using System.Security;
using System.Text;

namespace CraftedSolutions.MarBasGleaner.UI
{
    public class ConsoleFeedbackService : IFeedbackService
    {
        public static readonly string SeparatorLine = new('-', 50);

        public void DisplayError(string message)
        {
            WriteError(message);
        }

        public void DisplayInfo(string message)
        {
            WriteInfo(message);
        }

        public void DisplayMessage(string message, MessageSeparatorOption separatorOption = MessageSeparatorOption.None)
        {
            WriteMessage(message, separatorOption);
        }

        public void DisplayWarning(string message)
        {
            WriteWarning(message);
        }

        public string? GetText(string prompt, string? defaultValue = null)
        {
            return ReadText(prompt, defaultValue);
        }

        public SecureString GetSecureText(string prompt)
        {
            return ReadSecureText(prompt);
        }

        public static void WriteError(string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"{message}{Environment.NewLine}");
            }
            finally
            {
                Console.ResetColor();
            }
        }

        public static void WriteInfo(string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{message}{Environment.NewLine}");
            }
            finally
            {
                Console.ResetColor();
            }
        }

        public static void WriteMessage(string message, MessageSeparatorOption separatorOption = MessageSeparatorOption.None)
        {
            var buff = new StringBuilder();
            if (MessageSeparatorOption.Before == (MessageSeparatorOption.Before & separatorOption))
            {
                buff.Append(SeparatorLine);
                buff.Append(Environment.NewLine);
            }
            if (!string.IsNullOrEmpty(message))
            {
                buff.Append(message);
                buff.Append(Environment.NewLine);
            }
            if (MessageSeparatorOption.After == (MessageSeparatorOption.After & separatorOption))
            {
                buff.Append(SeparatorLine);
                buff.Append(Environment.NewLine);
            }
            Console.ResetColor();
            Console.Write(buff.ToString());
        }

        public static void WriteWarning(string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write($"{message}{Environment.NewLine}");
            }
            finally
            {
                Console.ResetColor();
            }
        }

        public static string? ReadText(string prompt, string? defaultValue = null)
        {
            WriteMessage(prompt);
            var result = Console.ReadLine();
            Console.Write(Environment.NewLine);
            return string.IsNullOrEmpty(result) ? defaultValue : result;
        }

        public static SecureString ReadSecureText(string prompt)
        {
            WriteMessage(prompt);
            var result = new SecureString();
            while (true)
            {
                ConsoleKeyInfo i = Console.ReadKey(true);
                if (i.Key == ConsoleKey.Enter)
                {
                    break;
                }
                else if (i.Key == ConsoleKey.Backspace)
                {
                    if (result.Length > 0)
                    {
                        result.RemoveAt(result.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                else if (i.KeyChar != '\u0000') // KeyChar == '\u0000' if the key pressed does not correspond to a printable character, e.g. F1, Pause-Break, etc
                {
                    result.AppendChar(i.KeyChar);
                    Console.Write("*");
                }
            }
            Console.Write(Environment.NewLine);
            return result;
        }
    }

}
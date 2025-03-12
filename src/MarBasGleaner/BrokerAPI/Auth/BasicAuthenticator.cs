
using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using CraftedSolutions.MarBasGleaner.BrokerAPI;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Auth
{
    internal class BasicAuthenticator : IAuthenticator
    {
        public const string SchemeName = "Basic";

        private const string ParamAuth = "token";

        public bool Authenticate(HttpClient client, ConnectionSettings? settings = null, bool storeCredentials = true)
        {
            var auth = null != settings && settings.AuthenticatorParams.TryGetValue(ParamAuth, out string? value) ? value : string.Empty;
            var authIsStored = !string.IsNullOrEmpty(auth);
            if (!authIsStored)
            {
                Console.Write($"Enter user name: {Environment.NewLine}");
                var user = Console.ReadLine();
                if (!string.IsNullOrEmpty(user))
                {
                    using var pwd = GetPassword();
                    if (0 < pwd.Length)
                    {
                        var cred = new NetworkCredential(user, pwd);
                        auth = Convert.ToBase64String(Encoding.Default.GetBytes($"{cred.UserName}:{cred.Password}"));
                    }
                }
                Console.Write(Environment.NewLine);
            }
            if (string.IsNullOrEmpty(auth))
            {
                return false;
            }
            if (null != settings)
            {
                settings.AuthenticatorType = GetType();
                if (storeCredentials && !authIsStored)
                {
                    settings.AuthenticatorParams.Add(ParamAuth, auth);
                }
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(SchemeName, auth);
            return true;
        }

        private static SecureString GetPassword()
        {
            Console.Write($"Enter password: {Environment.NewLine}");
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
            return result;
        }
    }
}

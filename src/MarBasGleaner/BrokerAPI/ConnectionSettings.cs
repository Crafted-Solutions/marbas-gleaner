using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using CraftedSolutions.MarBasGleaner.BrokerAPI.Auth;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI
{
    internal class ConnectionSettings
    {
        public static readonly Version MinimumAPIVersion = new(0, 1, 15);

        public required Uri BrokerUrl { get; set; }
        public bool IgnoreSslErrors { get; set; } = false;
        public string Authenticator
        {
            get => AuthenticatorType.FullName!;
            set => AuthenticatorType = Type.GetType(value) ?? throw new ArgumentException($"'{value}' is not a valid type");
        }
        [JsonIgnore]
        [IgnoreDataMember]
        public Type AuthenticatorType { get; set; } = typeof(BasicAuthenticator);
        public IDictionary<string, string> AuthenticatorParams { get; set; } = new Dictionary<string, string>();
    }
}

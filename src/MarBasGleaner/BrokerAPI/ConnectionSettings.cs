using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MarBasGleaner.BrokerAPI.Auth;

namespace MarBasGleaner.BrokerAPI
{
    internal class ConnectionSettings
    {
        public required Uri BrokerUrl { get; set; }
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

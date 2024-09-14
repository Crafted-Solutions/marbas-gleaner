using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using MarBasCommon;
using MarBasSchema;
using MarBasSchema.Grain;

namespace MarBasGleaner.BrokerAPI.Models
{
    internal class GrainYield : GrainPlain, ITypeConstraint
    {
        public string? TypeName { get; set; }
        [JsonIgnore]
        [IgnoreDataMember]
        public IIdentifiable? TypeDef { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
}

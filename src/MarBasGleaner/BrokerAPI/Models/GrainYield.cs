using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using CraftedSolutions.MarBasCommon;
using CraftedSolutions.MarBasSchema;
using CraftedSolutions.MarBasSchema.Grain;

namespace CraftedSolutions.MarBasGleaner.BrokerAPI.Models
{
    internal class GrainYield : GrainPlain, ITypeConstraint
    {
        public string? TypeName { get; set; }
        [JsonIgnore]
        [IgnoreDataMember]
        public IIdentifiable? TypeDef { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using MarBasSchema;
using MarBasSchema.Transport;
using MarBasSchema.IO;
using MarBasCommon.Json;

namespace MarBasGleaner.Json
{
    internal static class JsonDefaults
    {
        private static JsonSerializerOptions? _serializationOptions;
        private static JsonSerializerOptions? _deserializationOptions;

        public static JsonSerializerOptions SerializationOptions
        {
            get
            {
                if (_serializationOptions is not JsonSerializerOptions options)
                {
                    var result = new JsonSerializerOptions(JsonSerializerOptions.Default)
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        //DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull | JsonIgnoreCondition.WhenWritingDefault,
                        WriteIndented = true
                    };
                    result.Converters.Add(new IsoDateTimeJsonConverter());
                    options = Interlocked.CompareExchange(ref _serializationOptions, result, null) ?? result;
                }
                return options;
            }
        }

        public static JsonSerializerOptions DeserializationOptions
        {
            get
            {
                if (_deserializationOptions is not JsonSerializerOptions options)
                {
                    var result = new JsonSerializerOptions(SerializationOptions);
                    result.Converters.Add(new JsonStringEnumConverter());
                    result.Converters.Add(new InterfaceJsonConverter<ITypeConstraint, SimpleTypeConstraint>());
                    result.Converters.Add(new InterfaceJsonConverter<IAclEntryTransportable, AclEntryTransportable>());
                    result.Converters.Add(new InterfaceJsonConverter<IGrainLocalizedLayer, GrainLocalizedLayer>());
                    result.Converters.Add(new InterfaceJsonConverter<IStreamableContent, StreamableContent>());
                    options = Interlocked.CompareExchange(ref _deserializationOptions, result, null) ?? result;
                }
                return options;

            }
        }

    }
}

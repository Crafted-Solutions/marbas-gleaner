﻿using System.Text.Json;
using System.Text.Json.Serialization;
using CraftedSolutions.MarBasSchema;
using CraftedSolutions.MarBasSchema.Transport;
using CraftedSolutions.MarBasSchema.IO;
using CraftedSolutions.MarBasCommon.Json;
using CraftedSolutions.MarBasSchema.Broker;
using System.Text.Encodings.Web;

namespace CraftedSolutions.MarBasGleaner.Json
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
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
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
                    result.Converters.Add(new InterfaceJsonConverter<IBrokerOperationFeedback, BrokerOperationFeedback>());
                    options = Interlocked.CompareExchange(ref _deserializationOptions, result, null) ?? result;
                }
                return options;

            }
        }

    }
}

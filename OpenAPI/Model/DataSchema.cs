using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenApiBrowser.Model
{
    public record DataSchema
    {
        public string Description { get; set; }
        public dynamic Example { get; set; }

        public string Id { get; set; }

        [JsonPropertyName("properties")]
        public Dictionary<string, Property> Properties { get; set; }

        public string[] Required { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }

        public string Ref { get; set; }


        [JsonPropertyName("$ref")]
        public string RefJson { get; set; }

        public List<DataSchema> OneOf { get; set; }
        public List<DataSchema> AllOf { get; set; }
        public List<DataSchema> AnyOf { get; set; }
    }
}
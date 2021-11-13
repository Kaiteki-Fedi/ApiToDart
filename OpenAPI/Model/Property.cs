using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenApiBrowser.Model
{
    public class Property
    {
        public string Description { get; set; }
        public string Format { get; set; }

        public Property Items { get; set; }
        
        public Dictionary<string, Property> Properties { get; set; }

        public string[] Enum { get; set; }

        [JsonPropertyName("$ref")]
        public string JsonReference { get; set; }

        public bool Nullable { get; set; }

        public bool Optional { get; set; }

        [JsonPropertyName("ref")]
        public string Reference { get; set; }

        public string Type { get; set; }
    }
}
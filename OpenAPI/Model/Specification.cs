using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenApiBrowser.Model
{
    public class Specification
    {
        public Components Components { get; set; }

        public SpecificationInfo Info { get; set; }
        public Dictionary<string, Path> Paths { get; set; }

        // [JsonPropertyName("paths")]
        // public Server[] Servers { get; set; }
    }
}
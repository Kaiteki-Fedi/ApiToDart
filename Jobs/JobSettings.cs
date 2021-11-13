using System.Collections.Generic;

namespace ApiToDart
{
    public class JobSettings
    {
        public string ClassNamePrefix { get; set; }

        public bool Ignore { get; set; }

        /// <summary>
        /// Location of the file as if it were imported, no file name, only "folder/folder/folder"
        /// </summary>
        public string ImportPrefix { get; set; }

        public string OutputDirectory { get; set; }

        public Dictionary<string, string> TypeCorrections { get; set; }
        public Dictionary<string, bool> NullabilityCorrections { get; set; }
    }
}
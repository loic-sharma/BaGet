using System.Collections.Generic;

namespace BaGet.Core.Entities
{
    public class SourceCodeAssembly
    {
        public int Key { get; set; }

        public string Display { get; set; }

        public string Framework { get; set; }

        public int PackageKey { get; set; }

        public Package Package { get; set; }

        public List<SourceCodeType> Types { get; set; }

        public SourceCodeAssembly()
        {
            Types = new List<SourceCodeType>();
        }
    }
}

using System.Collections.Generic;

namespace BaGet.Core.Entities
{
    public class SourceCodeType
    {
        public int Key { get; set; }

        public string FullName { get; set; }

        public string Display { get; set; }

        public string CSharp { get; set; }

        public int AssemblyKey { get; set; }

        public List<SourceCodeMember> Members { get; set; }

        public SourceCodeAssembly Assembly { get; set; }

        public SourceCodeType()
        {
            Members = new List<SourceCodeMember>();
        }
    }
}

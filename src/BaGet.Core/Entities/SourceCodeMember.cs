using BaGet.Core.SourceCode;

namespace BaGet.Core.Entities
{
    public class SourceCodeMember
    {
        public int Key { get; set; }

        public AnalysisMemberKind MemberKind { get; set; }

        public string Name { get; set; }

        public string MemberType { get; set; }

        public string Display { get; set; }

        public int TypeKey { get; set; }

        public SourceCodeType Type { get; set; }
    }
}

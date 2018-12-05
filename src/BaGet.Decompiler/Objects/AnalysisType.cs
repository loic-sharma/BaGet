using System.Collections.Generic;

namespace BaGet.Decompiler.Objects
{
    public class AnalysisType
    {
        public string FullName { get; set; }

        public List<AnalysisMember> Members { get; set; }

        public string Display { get; set; }

        public string CSharp { get; set; }

        public AnalysisType()
        {
            Members = new List<AnalysisMember>();
        }
    }
}
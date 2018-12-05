using System.Collections.Generic;

namespace BaGet.Decompiler.Objects
{
    public class AnalysisAssembly 
    {
        public List<AnalysisType> Types { get; set; }

        public string Display { get; set; }

        public AnalysisAssembly()
        {
            Types = new List<AnalysisType>();
        }
    }
}
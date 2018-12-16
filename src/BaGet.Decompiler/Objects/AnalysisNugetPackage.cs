using System.Collections.Generic;

namespace BaGet.Decompiler.Objects
{
    public class AnalysisNugetPackage
    {
        public List<AnalysisNugetAssembly> Assemblies { get; set; }

        public AnalysisNugetPackage()
        {
            Assemblies = new List<AnalysisNugetAssembly>();
        }
    }
}

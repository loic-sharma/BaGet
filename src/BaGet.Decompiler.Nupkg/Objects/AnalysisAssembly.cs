using System.Collections.Generic;

namespace BaGet.Decompiler.Nupkg.Objects
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
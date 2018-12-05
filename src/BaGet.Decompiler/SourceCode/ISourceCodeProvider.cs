using BaGet.Decompiler.Objects;
using Mono.Cecil;

namespace BaGet.Decompiler.SourceCode
{
    public interface ISourceCodeProvider
    {
        bool TryFillSources(ModuleDefinition module, AnalysisAssembly assembly, string assemblyFile, string assemblyPdb);
    }
}
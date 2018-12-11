using BaGet.Decompiler.Objects;
using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Decompiler.SourceCode
{
    public interface ISourceCodeProvider
    {
        bool TryFillSources(IModule module, AnalysisAssembly assembly, string assemblyFile, string assemblyPdb);
    }
}
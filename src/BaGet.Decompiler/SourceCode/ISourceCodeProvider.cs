using BaGet.Decompiler.Objects;
using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Decompiler.SourceCode
{
    internal interface ISourceCodeProvider
    {
        bool TryFillSources(IModule module, AnalysisAssembly assembly);
    }
}

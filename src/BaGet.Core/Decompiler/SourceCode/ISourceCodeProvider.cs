using BaGet.Core.Entities;
using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Core.Decompiler.SourceCode
{
    internal interface ISourceCodeProvider
    {
        bool TryFillSources(IModule module, SourceCodeAssembly assembly);
    }
}

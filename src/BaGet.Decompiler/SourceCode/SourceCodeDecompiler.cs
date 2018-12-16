using System;
using BaGet.Decompiler.Objects;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Decompiler.SourceCode
{
    internal class SourceCodeDecompiler : ISourceCodeProvider
    {
        private readonly CSharpDecompiler _decompiler;

        public SourceCodeDecompiler(CSharpDecompiler decompiler)
        {
            _decompiler = decompiler;
        }

        public bool TryFillSources(IModule module, AnalysisAssembly assembly)
        {
            try
            {
                foreach (var decompiledType in assembly.Types)
                {
                    var fullTypeName = new FullTypeName(decompiledType.FullName);
                    var ast = _decompiler.DecompileType(fullTypeName);

                    var source = ast.ToString();

                    decompiledType.CSharp = source;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

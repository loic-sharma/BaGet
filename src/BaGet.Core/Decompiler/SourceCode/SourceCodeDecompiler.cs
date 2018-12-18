using System;
using BaGet.Core.Entities;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Core.Decompiler.SourceCode
{
    internal class SourceCodeDecompiler : ISourceCodeProvider
    {
        private readonly CSharpDecompiler _decompiler;

        public SourceCodeDecompiler(CSharpDecompiler decompiler)
        {
            _decompiler = decompiler ?? throw new ArgumentNullException(nameof(decompiler));
        }

        public bool TryFillSources(IModule module, SourceCodeAssembly assembly)
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

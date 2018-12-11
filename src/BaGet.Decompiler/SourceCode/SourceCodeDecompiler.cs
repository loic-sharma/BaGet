using System;
using BaGet.Decompiler.Objects;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Decompiler.SourceCode
{
    public class SourceCodeDecompiler : ISourceCodeProvider
    {
        private readonly CSharpDecompiler _decompiler;

        public SourceCodeDecompiler(CSharpDecompiler decompiler)
        {
            _decompiler = decompiler;
        }

        public bool TryFillSources(IModule module, AnalysisAssembly assembly, string assemblyFile, string assemblyPdb)
        {
            try
            {
                foreach (AnalysisType decompiledType in assembly.Types)
                {
                    FullTypeName fullTypeName = new FullTypeName(decompiledType.FullName);
                    SyntaxTree ast = _decompiler.DecompileType(fullTypeName);

                    string source = ast.ToString();

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
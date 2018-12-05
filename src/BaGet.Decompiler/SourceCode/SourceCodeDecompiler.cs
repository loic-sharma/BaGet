using System;
using BaGet.Decompiler.Objects;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Syntax;
using Mono.Cecil;

namespace BaGet.Decompiler.SourceCode
{
    public class SourceCodeDecompiler : ISourceCodeProvider
    {
        private readonly DecompilerSettings _decompilerTypeSettings;

        public SourceCodeDecompiler()
        {
            _decompilerTypeSettings = new DecompilerSettings
            {
                UseExpressionBodyForCalculatedGetterOnlyProperties = false,
                ShowXmlDocumentation = false
            };
        }

        public bool TryFillSources(ModuleDefinition module, AnalysisAssembly assembly, string assemblyFile, string assemblyPdb)
        {
            CSharpDecompiler decompiler = new CSharpDecompiler(module, _decompilerTypeSettings);

            try
            {
                foreach (AnalysisType decompiledType in assembly.Types)
                {
                    TypeDefinition type = module.GetType(decompiledType.FullName);
                    SyntaxTree ast = decompiler.Decompile(type);

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
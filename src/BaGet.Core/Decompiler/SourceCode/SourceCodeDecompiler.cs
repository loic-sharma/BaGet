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

        public bool TryFillSources(IModule module, SourceCodeType type)
        {
            try
            {
                var fullTypeName = new FullTypeName(type.FullName);
                var ast = _decompiler.DecompileType(fullTypeName);

                var source = ast.ToString();

                type.CSharp = source;

                return !string.IsNullOrEmpty(type.CSharp);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

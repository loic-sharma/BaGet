using ICSharpCode.Decompiler.Metadata;

namespace BaGet.Core.Decompiler.Decompilation
{
    internal class NullAssmblyResolver : IAssemblyResolver
    {
        public PEFile Resolve(IAssemblyReference reference)
        {
            return null;
        }

        public PEFile ResolveModule(PEFile mainModule, string moduleName)
        {
            return null;
        }
    }
}

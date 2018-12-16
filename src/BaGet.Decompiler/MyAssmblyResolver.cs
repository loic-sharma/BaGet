using ICSharpCode.Decompiler.Metadata;

namespace BaGet.Decompiler
{
    internal class MyAssmblyResolver : IAssemblyResolver
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
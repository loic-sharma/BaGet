using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaGet.Decompiler.Decompilation;
using BaGet.Decompiler.Objects;
using BaGet.Decompiler.SourceCode;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.Output;
using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Decompiler
{
    public class AssemblyDecompilerService
    {
        private readonly Filter _filter;
        private readonly CSharpAmbience _ambience;

        private readonly List<ISourceCodeProvider> _globalSourceCodeProviders;

        public AssemblyDecompilerService()
        {
            _ambience = new CSharpAmbience
            {
                ConversionFlags = ConversionFlags.StandardConversionFlags
            };

            _filter = new Filter();

            _globalSourceCodeProviders = new List<ISourceCodeProvider>();
        }

        public AnalysisAssembly AnalyzeAssembly(Stream assembly, Stream pdb = null, Stream documentationXml = null)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            List<ISourceCodeProvider> localSourceCodeProviders = new List<ISourceCodeProvider>();

            AnalysisAssembly res;
            try
            {
                string assemblyFile = Path.Combine(tempDir, "Binary.dll");
                string assemblyPdb = Path.Combine(tempDir, "Binary.pdb");

                using (FileStream fs = File.Create(assemblyFile))
                    assembly.CopyTo(fs);

                if (pdb != null)
                {
                    using (FileStream fs = File.Create(assemblyPdb))
                        pdb.CopyTo(fs);
                }

                // TODO: Load PDB from memory
                // TODO: Load module from memory
                var decompiler = new CSharpDecompiler(assemblyFile, new DecompilerSettings(LanguageVersion.Latest));
                res = DecompileAssembly(decompiler.TypeSystem.MainModule);

                // Read documentation
                // TODO Read documentation

                // Fetch sources
                localSourceCodeProviders.Add(new SourceCodeDecompiler(decompiler));

                foreach (ISourceCodeProvider provider in _globalSourceCodeProviders.Concat(localSourceCodeProviders))
                {
                    provider.TryFillSources(decompiler.TypeSystem.MainModule, res, assemblyFile, assemblyPdb);
                }
            }
            finally
            {
                // TODO: Properly close CSharpDecompiler
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception)
                {
                }
            }

            return res;
        }

        private AnalysisAssembly DecompileAssembly(MetadataModule module)
        {
            AnalysisAssembly res = new AnalysisAssembly();

            // Process all types
            foreach (var type in module.TypeDefinitions.Where(_filter.Include))
            {
                AnalysisType analysisType = new AnalysisType
                {
                    FullName = type.FullTypeName.ToString(),
                    Display = _ambience.ConvertSymbol(type)
                };

                res.Types.Add(analysisType);

                // Methods, Constructors
                foreach (var method in type.Methods)
                {
                    if (!_filter.Include(method))
                        continue;

                    AnalysisMember analysisMethod = new AnalysisMember
                    {
                        MemberType = method.IsConstructor ? AnalysisMemberType.Constructor : AnalysisMemberType.Method,
                        Name = method.Name,
                        Type = _ambience.ConvertType(method.ReturnType),
                        Display = _ambience.ConvertSymbol(method)
                    };

                    analysisType.Members.Add(analysisMethod);
                }

                // Properties, Fields, Events
                foreach (var member in type.Properties.Where(_filter.Include).OfType<IMember>()
                    .Concat(type.Fields.Where(_filter.Include))
                    .Concat(type.Events.Where(_filter.Include)))
                {
                    AnalysisMemberType analysisMemberType;
                    switch (member)
                    {
                        case IProperty _:
                            analysisMemberType = AnalysisMemberType.Property;
                            break;
                        case IEvent _:
                            analysisMemberType = AnalysisMemberType.Event;
                            break;
                        case IField _:
                            analysisMemberType = AnalysisMemberType.Field;
                            break;
                        default:
                            throw new Exception();
                    }

                    AnalysisMember analysisProperty = new AnalysisMember
                    {
                        MemberType = analysisMemberType,
                        Name = member.Name,
                        Type = _ambience.ConvertType(member.ReturnType),
                        Display = _ambience.ConvertSymbol(member)
                    };

                    analysisType.Members.Add(analysisProperty);
                }
            }

            return res;
        }
    }
}
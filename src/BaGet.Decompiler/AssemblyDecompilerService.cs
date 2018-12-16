using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using BaGet.Decompiler.Decompilation;
using BaGet.Decompiler.Objects;
using BaGet.Decompiler.SourceCode;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.Metadata;
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

            // TODO: Make source code provider for SourceLink
            // TODO: Make source code provider for embedded source in nupkg
            // TODO: Make source code provider for embedded source in pdb
        }

        public AnalysisAssembly AnalyzeAssembly(Stream assembly, Stream pdb = null, Stream documentationXml = null)
        {
            var localSourceCodeProviders = new List<ISourceCodeProvider>();

            var assemblyPe = new PEFile("Binary.dll", assembly, PEStreamOptions.PrefetchEntireImage);

            // TODO: Read PDB's (are they even needed?)
            //PEFile pdbPe = null;
            //if (pdb != null)
            //    pdbPe = new PEFile("Binary.pdb", pdb, PEStreamOptions.PrefetchEntireImage);

            var decompiler = new CSharpDecompiler(assemblyPe, new MyAssmblyResolver(), new DecompilerSettings(LanguageVersion.Latest));
            var res = DecompileAssembly(decompiler.TypeSystem.MainModule);

            // Read documentation
            // TODO Read documentation

            // Fetch sources
            localSourceCodeProviders.Add(new SourceCodeDecompiler(decompiler));

            foreach (var provider in _globalSourceCodeProviders.Concat(localSourceCodeProviders))
                provider.TryFillSources(decompiler.TypeSystem.MainModule, res);

            return res;
        }

        private AnalysisAssembly DecompileAssembly(MetadataModule module)
        {
            var res = new AnalysisAssembly
            {
                Display = module.FullAssemblyName
            };

            // Process all types
            foreach (var type in module.TypeDefinitions.Where(_filter.Include))
            {
                var analysisType = new AnalysisType
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

                    var analysisMethod = new AnalysisMember
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

                    var analysisProperty = new AnalysisMember
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

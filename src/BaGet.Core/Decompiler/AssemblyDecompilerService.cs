using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using BaGet.Core.Decompiler.Decompilation;
using BaGet.Core.Decompiler.SourceCode;
using BaGet.Core.Entities;
using BaGet.Core.SourceCode;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Output;
using ICSharpCode.Decompiler.TypeSystem;

namespace BaGet.Core.Decompiler
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

        public SourceCodeAssembly AnalyzeAssembly(Stream assembly, Stream pdb = null, Stream documentationXml = null)
        {
            var localSourceCodeProviders = new List<ISourceCodeProvider>();

            var assemblyPe = new PEFile("Binary.dll", assembly, PEStreamOptions.PrefetchEntireImage);

            // TODO: Read PDB's (are they even needed?)
            //PEFile pdbPe = null;
            //if (pdb != null)
            //    pdbPe = new PEFile("Binary.pdb", pdb, PEStreamOptions.PrefetchEntireImage);

            var decompiler = new CSharpDecompiler(assemblyPe, new NullAssemblyResolver(), new DecompilerSettings(LanguageVersion.Latest));
            var res = DecompileAssembly(decompiler.TypeSystem.MainModule);

            // Read documentation
            // TODO Read documentation

            // Fetch sources
            localSourceCodeProviders.Add(new SourceCodeDecompiler(decompiler));

            foreach (var provider in _globalSourceCodeProviders.Concat(localSourceCodeProviders))
                provider.TryFillSources(decompiler.TypeSystem.MainModule, res);

            return res;
        }

        private SourceCodeAssembly DecompileAssembly(MetadataModule module)
        {
            var res = new SourceCodeAssembly
            {
                Display = module.FullAssemblyName
            };

            // Process all types
            foreach (var type in module.TypeDefinitions.Where(_filter.Include))
            {
                var analysisType = new SourceCodeType
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

                    var analysisMethod = new SourceCodeMember
                    {
                        MemberKind = method.IsConstructor ? AnalysisMemberKind.Constructor : AnalysisMemberKind.Method,
                        Name = method.Name,
                        MemberType = _ambience.ConvertType(method.ReturnType),
                        Display = _ambience.ConvertSymbol(method)
                    };

                    analysisType.Members.Add(analysisMethod);
                }

                // Properties, Fields, Events
                foreach (var member in type.Properties.Where(_filter.Include).OfType<IMember>()
                    .Concat(type.Fields.Where(_filter.Include))
                    .Concat(type.Events.Where(_filter.Include)))
                {
                    AnalysisMemberKind analysisMemberKind;
                    switch (member)
                    {
                        case IProperty _:
                            analysisMemberKind = AnalysisMemberKind.Property;
                            break;
                        case IEvent _:
                            analysisMemberKind = AnalysisMemberKind.Event;
                            break;
                        case IField _:
                            analysisMemberKind = AnalysisMemberKind.Field;
                            break;
                        default:
                            throw new Exception();
                    }

                    var analysisProperty = new SourceCodeMember
                    {
                        MemberKind = analysisMemberKind,
                        Name = member.Name,
                        MemberType = _ambience.ConvertType(member.ReturnType),
                        Display = _ambience.ConvertSymbol(member)
                    };

                    analysisType.Members.Add(analysisProperty);
                }
            }

            return res;
        }
    }
}

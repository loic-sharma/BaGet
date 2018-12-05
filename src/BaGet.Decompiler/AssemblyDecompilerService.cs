using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaGet.Decompiler.Decompilation;
using BaGet.Decompiler.Objects;
using BaGet.Decompiler.SourceCode;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Syntax;
using Mono.Cecil;

namespace BaGet.Decompiler
{
    public class AssemblyDecompilerService
    {
        private readonly DecompilerSettings _decompilerMembersSettings;
        private readonly Filter _filter;

        private readonly List<ISourceCodeProvider> _sourceCodeProviders;

        public AssemblyDecompilerService()
        {
            _decompilerMembersSettings = new DecompilerSettings
            {
                //DecompileMemberBodies = false,    TODO: https://github.com/icsharpcode/ILSpy/issues/1332
                UsingDeclarations = false,
                ShowXmlDocumentation = false
            };

            _filter = new Filter();

            _sourceCodeProviders = new List<ISourceCodeProvider>
            {
                new SourceCodeDecompiler()
            };
        }

        public AnalysisAssembly AnalyzeAssembly(Stream assembly, Stream pdb = null, Stream documentationXml = null)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

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

                using (ModuleDefinition module = UniversalAssemblyResolver.LoadMainModule(assemblyFile, false, true))
                {
                    res = DecompileAssembly(module);

                    // Read documentation
                    // TODO Read documentation

                    // Fetch sources
                    foreach (ISourceCodeProvider provider in _sourceCodeProviders)
                    {
                        provider.TryFillSources(module, res, assemblyFile, assemblyPdb);
                    }
                }
            }
            finally
            {
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

        private AnalysisAssembly DecompileAssembly(ModuleDefinition module)
        {
            AnalysisAssembly res = new AnalysisAssembly();

            CSharpDecompiler decompilerMembers = new CSharpDecompiler(module, _decompilerMembersSettings);

            // Process all types
            foreach (TypeDefinition type in module.Types.Where(_filter.Include).Take(300))
            {
                string typeName = type.FullName;

                SyntaxTree ast = decompilerMembers.Decompile(type);
                string declaration = DeclarationRenderer.RenderTypeDeclaration(ast);

                AnalysisType analysisType = new AnalysisType
                {
                    FullName = typeName,
                    Display = declaration
                };

                res.Types.Add(analysisType);

                // Methods, Constructors
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!_filter.Include(method))
                        continue;

                    SyntaxTree methodAst = decompilerMembers.Decompile(method);

                    AstNode methodType = methodAst.DescendantNodes().First(s => s.NodeType == NodeType.TypeReference);
                    string methodDeclaration = methodAst.ToString().Trim();

                    AnalysisMember analysisMethod = new AnalysisMember
                    {
                        MemberType = method.IsConstructor ? AnalysisMemberType.Constructor : AnalysisMemberType.Method,
                        Name = method.Name,
                        Type = methodType.ToString(),
                        Display = methodDeclaration
                    };

                    analysisType.Members.Add(analysisMethod);
                }

                // Properties, Fields, Events
                foreach (IMemberDefinition member in type.Properties.Where(_filter.Include).OfType<IMemberDefinition>()
                    .Concat(type.Fields.Where(_filter.Include))
                    .Concat(type.Events.Where(_filter.Include)))
                {
                    SyntaxTree memberAst = decompilerMembers.Decompile(member);

                    string memberType = memberAst.DescendantNodes()
                        .FirstOrDefault(s => s.NodeType == NodeType.TypeReference)?.ToString();
                    string memberDeclaration = memberAst.ToString().Trim();

                    if (memberType == null)
                    {
                        if (type.IsEnum)
                            memberType = typeName;
                        else
                            throw new Exception();
                    }

                    AnalysisMemberType analysisMemberType;
                    switch (member)
                    {
                        case PropertyDefinition _:
                            analysisMemberType = AnalysisMemberType.Property;
                            break;
                        case EventDefinition _:
                            analysisMemberType = AnalysisMemberType.Event;
                            break;
                        case FieldDefinition _:
                            analysisMemberType = AnalysisMemberType.Field;
                            break;
                        default:
                            throw new Exception();
                    }

                    AnalysisMember analysisProperty = new AnalysisMember
                    {
                        MemberType = analysisMemberType,
                        Name = member.Name,
                        Type = memberType,
                        Display = memberDeclaration
                    };

                    analysisType.Members.Add(analysisProperty);
                }
            }

            return res;
        }
    }
}
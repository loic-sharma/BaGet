using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BaGet.Core.Entities;
using NuGet.Packaging;

namespace BaGet.Core.Decompiler
{
    public class NugetDecompilerService
    {
        private readonly AssemblyDecompilerService _assemblyDecompilerService;

        public NugetDecompilerService(AssemblyDecompilerService assemblyDecompilerService)
        {
            _assemblyDecompilerService = assemblyDecompilerService;
        }

        public IReadOnlyList<SourceCodeAssembly> AnalyzePackage(PackageArchiveReader package)
        {
            // TODO: Read pdb's
            // TODO: Read xml docs

            var res = new List<SourceCodeAssembly>();

            foreach (var item in package.GetReferenceItems())
            {
                // TODO: Can a reference be multiple files?
                var file = item.Items.Single();
                var folder = file.Substring(0, file.Length - Path.GetFileName(file).Length - 1);

                var assemblyStream = new MemoryStream();
                using (var tmp = package.GetStream(file))
                    tmp.CopyTo(assemblyStream);

                assemblyStream.Seek(0, SeekOrigin.Begin);

                var otherFiles = package.GetFiles(folder).ToList();

                var pdbFileName = Path.ChangeExtension(file, ".pdb");
                var pdbFile = otherFiles.Contains(pdbFileName, StringComparer.OrdinalIgnoreCase);

                MemoryStream pdbStream = null;
                if (pdbFile)
                {
                    pdbStream = new MemoryStream();
                    using (var tmp = package.GetStream(pdbFileName))
                        tmp.CopyTo(pdbStream);

                    pdbStream.Seek(0, SeekOrigin.Begin);
                }

                var xmlDocFileName = Path.ChangeExtension(file, ".xml");
                var xmlDocFile = otherFiles.Contains(xmlDocFileName, StringComparer.OrdinalIgnoreCase);

                MemoryStream xmlDocStream = null;
                if (xmlDocFile)
                {
                    xmlDocStream = new MemoryStream();
                    using (var tmp = package.GetStream(xmlDocFileName))
                        tmp.CopyTo(xmlDocStream);

                    xmlDocStream.Seek(0, SeekOrigin.Begin);
                }

                var analysis = _assemblyDecompilerService.AnalyzeAssembly(assemblyStream, pdbStream, xmlDocStream);

                analysis.Framework = item.TargetFramework.DotNetFrameworkName;

                res.Add(analysis);
            }

            return res;
        }
    }
}

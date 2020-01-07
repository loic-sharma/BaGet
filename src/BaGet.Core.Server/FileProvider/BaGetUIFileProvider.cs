using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace BaGet.Core.Server.FileProvider
{
    public class BaGetUIFileProvider : IFileProvider
    {
        private readonly string _absoluteRoot;
        private readonly BaGetOptions _bagetOptions;

        private BaGetRootDirectory _rootDirectory;


        public BaGetUIFileProvider(string absoluteRoot, BaGetOptions bagetOptions)
        {
            _absoluteRoot = Path.GetFullPath(absoluteRoot);
            if(!_absoluteRoot.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                _absoluteRoot = _absoluteRoot + Path.DirectorySeparatorChar;
            }
            if (!Directory.Exists(_absoluteRoot))
            {
                throw new DirectoryNotFoundException(_absoluteRoot);
            }
            _bagetOptions = bagetOptions;
            _rootDirectory = new BaGetRootDirectory(_absoluteRoot, bagetOptions);
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            return _rootDirectory.getDirectoryContents(subpath);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            return _rootDirectory.GetFileInfo(subpath);
        }

        public IChangeToken Watch(string filter)
        {
            return _rootDirectory.GetChangeToken(filter);
            
        }

    }
}

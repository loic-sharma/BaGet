using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using BaGet.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace BaGet.Hosting
{
    public class BaGetDirectoryContents: IDirectoryContents
    {
        private readonly string _directoryPath;
        private readonly BaGetRootDirectory _rootDirectory;
        private IEnumerable<IFileInfo> _fileInfos;

        public BaGetDirectoryContents(string directoryPath, BaGetRootDirectory rootDirectory)
        {
            _directoryPath = directoryPath;
            _rootDirectory = rootDirectory;
        }

        public bool Exists => Directory.Exists(_directoryPath);

        public IEnumerator<IFileInfo> GetEnumerator()
        {
            Initialize();
            return _fileInfos.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            Initialize();
            return _fileInfos.GetEnumerator();
        }

        private void Initialize()
        {
            try
            {
                _fileInfos = new DirectoryInfo(_directoryPath).EnumerateFileSystemInfos().Select(info =>
                {
                    if(info is FileInfo file)
                    {
                        return _rootDirectory.AddFile(info.FullName);
                    }
                    if(info is DirectoryInfo dir)
                    {
                        return new PhysicalDirectoryInfo(dir);
                    }
                    throw new ArgumentException("No matching Info found");
                });
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException || ex is IOException)
            {
                _fileInfos = Enumerable.Empty<IFileInfo>();
            }
        }


    }

    public class BaGetRootDirectory
    {
        private ConcurrentDictionary<string, BaGetFileInfo> _fileInfoCache;

        private readonly BaGetOptions _bagetOptions;

        public readonly ImmutableList<string> IN_MEMORY_FILTERS = ImmutableList<string>.Empty.Add("index.html").Add(".js");

        public string RootPath { get; private set; }

        internal PhysicalFilesWatcher FileWatcher { get; private set; }

        public BaGetRootDirectory(string rootPath, BaGetOptions bagetOptions)
        {
            _bagetOptions = bagetOptions;
            RootPath = rootPath;
            _fileInfoCache = new ConcurrentDictionary<string, BaGetFileInfo>();
            FileWatcher = new PhysicalFilesWatcher(rootPath, new FileSystemWatcher(rootPath), true);
        }

        internal IFileInfo AddFile(string absolutePath)
        {
            if (IN_MEMORY_FILTERS.Any(x => absolutePath.EndsWith(x)))
            {
                if (!_fileInfoCache.ContainsKey(absolutePath))
                {
                    lock (_fileInfoCache)
                    {
                        if (!_fileInfoCache.ContainsKey(absolutePath))
                        {
                            _fileInfoCache.AddOrUpdate(absolutePath, new BaGetFileInfo(new FileInfo(absolutePath), _bagetOptions), (x, y) => y);
                        }
                    }
                }
                return _fileInfoCache[absolutePath];
            } else
            {
                return new PhysicalFileInfo(new FileInfo(absolutePath));
            }
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var rootedPath = checkAndRootPath(subpath);
            if (rootedPath != null)
            {
                    return AddFile(rootedPath);
            }
            return new FileNotFoundInfo();
        }

        private string checkAndRootPath(string subpath)
        {
            subpath = RemoveTrailingSlash(subpath);
            string rootedPath = null;
            try
            {
                rootedPath = Path.GetFullPath(Path.Combine(RootPath, subpath));
            }
            catch
            {
                return null;
            }
            if (!rootedPath.StartsWith(RootPath))
            {
                return null;
            }
            return rootedPath;
            
        }

        private string RemoveTrailingSlash(string subpath)
        {
            return subpath.TrimStart('/');
        }



        public IDirectoryContents getDirectoryContents(string subpath)
        {
            var rootedPath = checkAndRootPath(subpath);
            if(rootedPath != null)
            {
                return new BaGetDirectoryContents(rootedPath, this);
            } else
            {
                return NotFoundDirectoryContents.Singleton;
            }
        }

        public IChangeToken GetChangeToken(string filter)
        {
            return FileWatcher.CreateFileChangeToken(filter);
        }
    }
}

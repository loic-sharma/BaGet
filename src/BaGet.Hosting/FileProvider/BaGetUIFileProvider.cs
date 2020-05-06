using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Primitives;

namespace BaGet.Hosting
{
    public class BaGetUIFileProvider : IFileProvider
    {
        private readonly DirectoryInfo _root;
        private readonly string _pathBase;
        private readonly PhysicalFilesWatcher _watcher;
        private readonly Matcher _matcher;
        private readonly ConcurrentDictionary<string, CachedFileInfo> _fileCache;

        public BaGetUIFileProvider(DirectoryInfo root, string pathBase)
        {
            _root = root;
            _pathBase = pathBase;
            _watcher = new PhysicalFilesWatcher(root.FullName, new FileSystemWatcher(root.FullName), true);
            _matcher = new Matcher().AddInclude("**/index.html").AddInclude("**/*.js");
            _fileCache = new ConcurrentDictionary<string, CachedFileInfo>();
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            return new DirectoryContents(this, new DirectoryInfo(Path.Combine(_root.FullName, subpath.TrimStart('/'))));
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            var info = new FileInfo(Path.Combine(_root.FullName, subpath.TrimStart('/')));
            if (info.Exists)
            {
                var relative = Path.GetRelativePath(_root.FullName, info.FullName);
                if (_matcher.Match(relative).HasMatches)
                {
                    if (_fileCache.TryGetValue(relative, out var file) && file.LastModified >= info.LastWriteTime)
                    {
                        return file;
                    }
                    else // Add a missing entry or update an outdated file
                    {
                        return AddOrUpdate(info, relative);
                    }
                }
            }

            return new PhysicalFileInfo(info);
        }

        public IChangeToken Watch(string filter)
        {
            return _watcher.CreateFileChangeToken(filter);
        }

        private CachedFileInfo AddOrUpdate(FileInfo info, string relative)
        {
            var content = File.ReadAllText(info.FullName, Encoding.UTF8)
                .Replace("__BAGET_PATH_BASE_PLACEHOLDER__", _pathBase)
                .Replace("__BAGET_PLACEHOLDER_API_URL__", _pathBase);

            var file = new CachedFileInfo(info, Encoding.UTF8.GetBytes(content));

            return _fileCache.AddOrUpdate(relative, file, (_, existing) =>
            {
                if (file.LastModified > existing.LastModified)
                    return file;
                else
                    return existing;
            });
        }

        private class DirectoryContents : IDirectoryContents
        {
            private readonly BaGetUIFileProvider _provider;
            private readonly DirectoryInfo _directory;

            public DirectoryContents(BaGetUIFileProvider provider, DirectoryInfo directory)
            {
                _provider = provider;
                _directory = directory;
            }

            public bool Exists => _directory.Exists;

            public IEnumerator<IFileInfo> GetEnumerator()
            {
                if (Exists)
                {
                    foreach (var info in _directory.EnumerateFileSystemInfos())
                    {
                        if (info is FileInfo)
                            yield return _provider.GetFileInfo(Path.GetRelativePath(_provider._root.FullName, info.FullName));
                        if (info is DirectoryInfo directory)
                            yield return new PhysicalDirectoryInfo(directory);
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private class CachedFileInfo : IFileInfo
        {
            private readonly byte[] _content;

            public CachedFileInfo(FileInfo file, byte[] content)
            {
                _content = content;

                Length = content.Length;
                Name = file.Name;
                LastModified = file.LastWriteTime;
            }

            public bool Exists => true;
            public long Length { get; }
            public string PhysicalPath => null; // Prevent ASP.NET Core from loading the file from disk
            public string Name { get; }
            public DateTimeOffset LastModified { get; }
            public bool IsDirectory => false;

            public Stream CreateReadStream()
            {
                return new MemoryStream(_content, false);
            }
        }
    }
}

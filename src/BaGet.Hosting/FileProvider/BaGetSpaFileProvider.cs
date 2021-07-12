using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BaGet.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace BaGet.Hosting
{
    public class BaGetSpaFileProvider : IFileProvider
    {
        private readonly DirectoryInfo _root;
        private readonly IOptions<BaGetOptions> _options;
        private readonly PhysicalFilesWatcher _watcher;
        private readonly ConcurrentDictionary<string, IFileInfo> _fileCache;

        public BaGetSpaFileProvider(DirectoryInfo root, IOptions<BaGetOptions> options)
        {
            _root = root;
            _options = options;
            _watcher = new PhysicalFilesWatcher(root.FullName, new FileSystemWatcher(root.FullName), true);
            _fileCache = new ConcurrentDictionary<string, IFileInfo>();
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
                if (relative.EndsWith("index.html", StringComparison.Ordinal) || relative.EndsWith(".js", StringComparison.Ordinal))
                {
                    var pathBase = GetPathBase();

                    // Only return cached file if it hasn't been modified and the PathBase setting hasn't been changed.

                    if (_fileCache.TryGetValue(relative, out var file)
                        && file.LastModified >= info.LastWriteTime
                        && (!(file is MemoryFileInfo memoryFile) || memoryFile.PathBase == pathBase))
                    {
                        return file;
                    }
                    else
                    {
                        // Add a missing entry or update an outdated file.

                        var updated = BuildFileInfo(info, pathBase);

                        return _fileCache.AddOrUpdate(relative, updated, (_, existing) =>
                        {
                            if (file.LastModified > existing.LastModified)
                                return updated;
                            else
                                return existing;
                        });
                    }
                }
            }

            return new PhysicalFileInfo(info);
        }

        public IChangeToken Watch(string filter)
        {
            return _watcher.CreateFileChangeToken(filter);
        }

        private string GetPathBase()
        {
            var pathBase = _options.Value.PathBase;
            if (string.IsNullOrWhiteSpace(pathBase) || pathBase.Trim().Equals("/"))
            {
                return string.Empty;
            }
            else if (!pathBase.StartsWith("/"))
            {
                return "/" + pathBase.Trim().TrimEnd('/');
            }
            else
            {
                return pathBase;
            }
        }

        private IFileInfo BuildFileInfo(FileInfo file, string pathBase)
        {
            // Don't do anything if the file doens't have any placeholders.
            var rawContent = File.ReadAllText(file.FullName);
            if (!rawContent.Contains("__BAGET_PLACEHOLDER", StringComparison.Ordinal))
            {
                return new PhysicalFileInfo(file);
            }

            var replacedContent = Encoding.UTF8.GetBytes(
                rawContent
                    .Replace("__BAGET_PLACEHOLDER_API_URL__", pathBase)
                    .Replace("__BAGET_PLACEHOLDER_PATH_BASE__", pathBase));

            return new MemoryFileInfo(file, replacedContent, pathBase);
        }

        private class DirectoryContents : IDirectoryContents
        {
            private readonly BaGetSpaFileProvider _provider;
            private readonly DirectoryInfo _directory;

            public DirectoryContents(BaGetSpaFileProvider provider, DirectoryInfo directory)
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

        private class MemoryFileInfo : IFileInfo
        {
            private readonly byte[] _content;

            public MemoryFileInfo(FileInfo file, byte[] content, string pathBase)
            {
                _content = content;

                PathBase = pathBase;
                Length = content.Length;
                Name = file.Name;
                LastModified = file.LastWriteTime;
            }

            public string PathBase { get; }
            public bool Exists => true;
            public long Length { get; }
            public string Name { get; }
            public DateTimeOffset LastModified { get; }
            public bool IsDirectory => false;

            // Prevent ASP.NET Core from loading the file from disk
            public string PhysicalPath => null;

            public Stream CreateReadStream()
            {
                return new MemoryStream(_content, writable: false);
            }
        }
    }
}

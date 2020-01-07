using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace BaGet.Core.Server.FileProvider
{
    public class BaGetFileInfo : IFileInfo
    {
        public bool Exists { get; private set; }

        public long Length { get; private set; }

        public string PhysicalPath { get; private set; }

        public string Name { get; private set; }

        public DateTimeOffset LastModified { get; private set; }

        public bool IsDirectory => false;

        private readonly FileInfo _fileInfo;
        
        private BaGetOptions _bagetOptions;

        private byte[] _content;

        public BaGetFileInfo(FileInfo fileInfo, BaGetOptions bagetOptions)
        {
            _bagetOptions = bagetOptions;
            _fileInfo = fileInfo;
            Exists = _fileInfo.Exists;
            if (Exists)
            {
                LastModified = _fileInfo.LastWriteTime;
                Name = _fileInfo.Name;
                PhysicalPath = fileInfo.FullName;
                var content = readFile();
                if(NeedsContentUpdate(PhysicalPath))
                {
                    content = updateFileContentsWithPathBase(content); 
                }
                _content = Encoding.UTF8.GetBytes(content);
                Length = _content.Length;
            }
        }

        public Stream CreateReadStream()
        {
            var stream = new MemoryStream(_content, false);
            return stream;
        }

        private string updateFileContentsWithPathBase(string content)
        {

            return content.Replace("__BAGET_PATH_BASE_PLACEHOLDER__", getPathBase()).Replace("__BAGET_PLACEHOLDER_API_URL__", getPathBase());
        }

        private string readFile()
        {
            return File.ReadAllText(PhysicalPath, Encoding.UTF8);
        }

        private string getPathBase()
        {
            var pathBase = _bagetOptions.PathBase;
            if (string.IsNullOrWhiteSpace(pathBase) || pathBase.Trim().Equals("/"))
            {
                pathBase = "";
            }
            else
            {
                if (!pathBase.StartsWith("/"))
                {
                    pathBase = "/" + pathBase.Trim().TrimEnd('/');
                }
            }
            return pathBase;
        }

        private static bool NeedsContentUpdate(string path)
        {
            return path.EndsWith("index.html") || path.EndsWith(".js");
        }
    }

    public class FileNotFoundInfo : IFileInfo
    {
        public bool Exists => false;

        public long Length => 0;

        public string PhysicalPath => null;

        public string Name => null;

        public DateTimeOffset LastModified => DateTimeOffset.MinValue;

        public bool IsDirectory => false;

        public FileNotFoundInfo()
        {

        }

        public Stream CreateReadStream()
        {
            return null;
        }
    }
}

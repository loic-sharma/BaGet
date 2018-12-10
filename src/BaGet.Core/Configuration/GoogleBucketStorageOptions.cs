using System;
using System.Collections.Generic;
using System.Text;

namespace BaGet.Core.Configuration
{
    public class GoogleBucketStorageOptions : StorageOptions
    {
        public string BucketName { get; set; }
    }
}

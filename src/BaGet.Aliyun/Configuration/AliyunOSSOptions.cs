using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Aliyun.Configuration
{
    public class AliyunOSSOptions
    {
        [Required]
        public string AccessKey { get; set; }

        [Required]
        public string AccessKeySecret { get; set; }

        [Required]
        public string Endpoint { get; set; }

        [Required]
        public string Bucket { get; set; }

        public string Prefix { get; set; }
    }
}

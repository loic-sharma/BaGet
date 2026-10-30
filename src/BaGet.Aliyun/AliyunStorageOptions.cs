using System.ComponentModel.DataAnnotations;

using BaGet.Core;

namespace BaGet.Aliyun;

[RestartRequired]
public class AliyunStorageOptions
{
    [Required]
    [SensitiveSetting]
    public string? AccessKey { get; set; }

    [Required]
    [SensitiveSetting]
    public string? AccessKeySecret { get; set; }

    [Required]
    public string? Endpoint { get; set; }

    [Required]
    public string? Bucket { get; set; }

    public string? Prefix { get; set; }
}
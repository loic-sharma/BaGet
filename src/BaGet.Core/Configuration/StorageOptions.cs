namespace BaGet.Core;

[RestartRequired]
public class StorageOptions
{
    /// <summary>
    /// The package storage backend.
    /// </summary>
    [AllowedSettingValues("AliyunOss", "AwsS3", "AzureBlobStorage", "Filesystem", "GoogleCloud", "Null")]
    public string? Type { get; set; }
}
namespace BaGet.Core
{
    public class StorageOptions
    {
        public StorageType Type { get; set; }
    }

    public enum StorageType
    {
        FileSystem = 0,
        AzureBlobStorage = 1,
        AwsS3 = 2,
        GoogleCloud = 3,
        Null = 4,
        AliyunOss = 5,
    }
}

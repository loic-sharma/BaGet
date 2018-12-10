namespace BaGet.Core.Configuration
{
    public class StorageOptions
    {
        public StorageType Type { get; set; }
    }

    public enum StorageType
    {
        FileSystem = 0,
        AzureBlobStorage = 1,
        GoogleBucket = 2
    }
}

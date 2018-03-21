namespace BaGet.Configuration
{
    public class StorageOptions
    {
        public StorageType Type { get; set; }

        public string Path { get; set; }
    }

    public enum StorageType
    {
        FileSystem = 0,
        AzureBlobStorage = 1,
    }
}

namespace BaGet.Core
{
    public class Blob
    {
        public string Name { get; set; }
        public string PackageId { get; set; }
        public string Path { get; set; }

        public Blob(){}

        public Blob(string path)
        {
            Path = path;
        }
    }
}

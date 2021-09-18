namespace BaGet.Core
{
    public class TargetFramework
    {
        public int Key { get; set; }

        public string Moniker { get; set; }

        public int PackageKey { get; set; }
        public Package Package { get; set; }
    }
}

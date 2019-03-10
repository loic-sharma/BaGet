namespace BaGet.Core.Configuration
{
    public class HttpsOptions
    {
        public int Port { get; set; }

        public string CertificateFileName { get; set; }

        public string CertificatePassword { get; set; }
    }
}

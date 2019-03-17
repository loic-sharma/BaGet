using System;
using Microsoft.Extensions.Options;



namespace BaGet.Extensions
{
    public class BasicAuthenticationPostConfigureOptions : IPostConfigureOptions<BasicAuthenticationOptions>
    {
        public void PostConfigure(string name, BasicAuthenticationOptions options)
        {
            //Realm usage optional)
            //if (string.IsNullOrEmpty(options.Realm))
            //{
            //    throw new InvalidOperationException("Realm must be provided in options");
            //}
        }
    }
}

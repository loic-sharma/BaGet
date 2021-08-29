using System;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BaGet.Web
{
    public class UploadModel : PageModel
    {
        private readonly IUrlGenerator _url;

        public UploadModel(IUrlGenerator url)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
        }

        public string BaseUrl { get; set; }
        public string PublishUrl { get; set; }
        public string ServiceIndexUrl { get; set; }

        public void OnGet()
        {
            BaseUrl = Url.Page("/Index");
            PublishUrl = _url.GetPackagePublishResourceUrl();
            ServiceIndexUrl = _url.GetServiceIndexUrl();
        }
    }
}

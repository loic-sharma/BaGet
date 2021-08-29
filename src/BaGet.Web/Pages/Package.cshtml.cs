using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BaGet.Web
{
    public class PackageModel : PageModel
    {
        private readonly IPackageMetadataService _metadata;

        public PackageModel(IPackageMetadataService metadata)
        {
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        public string Id { get; set; }
        public string Version { get; set; }

        public void OnGetAsync(string id, string version)
        {
            Id = id;
            Version = version;
        }
    }
}

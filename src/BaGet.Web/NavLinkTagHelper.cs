using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BaGet.Web
{
    [HtmlTargetElement(Attributes = "nav-link")]
    public class NavLinkTagHelper : TagHelper
    {
        private readonly IHttpContextAccessor _accessor;

        public NavLinkTagHelper(IHttpContextAccessor accessor)
        {
            _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        }

        [HtmlAttributeName("asp-page")]
        public string Page { get; set; }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (IsActiveLink())
            {
                output.Attributes.SetAttribute("class", "active");
            }
        }

        private bool IsActiveLink()
        {
            var endpoint = _accessor.HttpContext?.GetEndpoint();
            var pageDescriptor = endpoint?.Metadata.GetMetadata<PageActionDescriptor>();

            if (pageDescriptor == null) return false;
            if (pageDescriptor.AreaName != null) return false;
            if (pageDescriptor.ViewEnginePath != Page) return false;

            return true;
        }
    }
}

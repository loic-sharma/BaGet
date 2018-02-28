using Microsoft.AspNetCore.Mvc;

namespace BaGet.Controllers
{
    public class UploadController : Controller
    {
        public void Upload()
        {
            HttpContext.Response.StatusCode = 201;
        }
    }
}

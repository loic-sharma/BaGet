using System.Collections.Generic;

namespace BaGet.Web.Application.Models.Packages;

public class DependencyGroupModel
{
    public string? Name { get; set; }
    public IReadOnlyList<DependencyModel>? Dependencies { get; set; }
}

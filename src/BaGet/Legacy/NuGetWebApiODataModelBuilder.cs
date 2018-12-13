using System;
using Microsoft.AspNet.OData.Builder;
using Microsoft.OData.Edm;

namespace BaGet.Legacy
{
    public class NuGetWebApiODataModelBuilder
    {
        private IEdmModel _model;

        public IEdmModel Model
        {
            get
            {
                if (_model == null)
                {
                    throw new InvalidOperationException("Must invoke Build method before accessing Model.");
                }
                return _model;
            }
        }

        public void Build()
        {
            var builder = new ODataConventionModelBuilder();

            var entity = builder.EntitySet<ODataPackage>("Packages");
            entity.EntityType.HasKey(pkg => pkg.Id);
            entity.EntityType.HasKey(pkg => pkg.Version);

            var searchAction = builder.Action("Search");
            searchAction.Parameter<string>("searchTerm");
            searchAction.Parameter<string>("targetFramework");
            searchAction.Parameter<bool>("includePrerelease");
            searchAction.ReturnsCollectionFromEntitySet<ODataPackage>("Packages");

            var findPackagesAction = builder.Action("FindPackagesById");
            findPackagesAction.Parameter<string>("id");
            findPackagesAction.ReturnsCollectionFromEntitySet<ODataPackage>("Packages");

            var getUpdatesAction = builder.Action("GetUpdates");
            getUpdatesAction.Parameter<string>("packageIds");
            getUpdatesAction.Parameter<bool>("includePrerelease");
            getUpdatesAction.Parameter<bool>("includeAllVersions");
            getUpdatesAction.Parameter<string>("targetFrameworks");
            getUpdatesAction.Parameter<string>("versionConstraints");
            getUpdatesAction.ReturnsCollectionFromEntitySet<ODataPackage>("Packages");

            _model = builder.GetEdmModel();
        }
    }
}

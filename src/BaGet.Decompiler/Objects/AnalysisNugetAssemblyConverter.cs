using AutoMapper;

namespace BaGet.Decompiler.Objects
{
    internal static class AnalysisNugetAssemblyConverter
    {
        private static readonly IMapper Mapper;

        static AnalysisNugetAssemblyConverter()
        {
            Mapper = new MapperConfiguration(config =>
            {
                config.CreateMissingTypeMaps = false;
                config.CreateMap<AnalysisAssembly, AnalysisNugetAssembly>();
            }).CreateMapper();
        }

        public static AnalysisNugetAssembly Convert(AnalysisAssembly source)
        {
            return Mapper.Map<AnalysisNugetAssembly>(source);
        }
    }
}
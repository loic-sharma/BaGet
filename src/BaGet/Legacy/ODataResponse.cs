namespace BaGet.Legacy
{
    public class ODataResponse<T>
    {
        private readonly string _serviceBaseUrl;
        private readonly T _entity;

        public ODataResponse(string serviceBaseUrl, T entity)
        {
            _serviceBaseUrl = serviceBaseUrl;
            _entity = entity;
        }

        public string ServiceBaseUrl => _serviceBaseUrl;

        public T Entity => _entity;
    }
}

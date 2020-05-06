using System.Net;

namespace BaGet.Azure
{
    using StorageException = Microsoft.WindowsAzure.Storage.StorageException;
    using TableStorageException = Microsoft.Azure.Cosmos.Table.StorageException;

    internal static class StorageExceptionExtensions
    {
        public static bool IsAlreadyExistsException(this StorageException e)
        {
            return e?.RequestInformation?.HttpStatusCode == (int?)HttpStatusCode.Conflict;
        }

        public static bool IsNotFoundException(this TableStorageException e)
        {
            return e?.RequestInformation?.HttpStatusCode == (int?)HttpStatusCode.NotFound;
        }

        public static bool IsAlreadyExistsException(this TableStorageException e)
        {
            return e?.RequestInformation?.HttpStatusCode == (int?)HttpStatusCode.Conflict;
        }

        public static bool IsPreconditionFailedException(this TableStorageException e)
        {
            return e?.RequestInformation?.HttpStatusCode == (int?)HttpStatusCode.PreconditionFailed;
        }
    }
}

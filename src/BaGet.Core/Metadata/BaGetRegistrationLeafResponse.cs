using System;
using System.Collections.Generic;
using BaGet.Protocol;

namespace BaGet.Core.Metadata
{
    public class BaGetRegistrationLeafResponse : RegistrationLeafResponse
    {
        public BaGetRegistrationLeafResponse(
            string registrationUri,
            bool listed,
            long downloads,
            string packageContentUrl,
            DateTimeOffset published,
            string registrationIndexUrl,
            IReadOnlyList<string> type = null)
          : base(registrationUri, listed, packageContentUrl, published, registrationIndexUrl, type)
        {
            Downloads = downloads;
        }

        public long Downloads { get; }
    }
}

using System;
using System.Collections.Generic;
using BaGet.Protocol;

namespace BaGet.Core.Metadata
{
    public class BaGetRegistrationLeafResponse : RegistrationLeafResponse
    {
        public long Downloads { get; set; }
    }
}

namespace BaGet.Core.Configuration
{
    public class BaGetJwtBearerOptions //we can't use the name "JwtBearerOptions" because it is just used inside aspnetcore and we can't reuse the same class here
    {
        public string Audience { get; set; }
        public bool ValidateAudience { get; set; } = true;
        public bool ValidateIssuer { get; set; } = true;
        public bool ValidateLifetime { get; set; } = true;
        public bool ValidateIssuerSigningKey { get; set; } = false;

        /// <summary>
        /// more then 55 characters needed - shorter results in exceptions 
        /// </summary>
        public string SymmetricSecurityKey { get; set; }
    }
}

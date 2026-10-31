namespace NumberForge.Api.Security;

// These limits apply to one API process. The deployment proxy should also enforce traffic limits.
public sealed class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public string[] KnownProxies { get; set; } = Array.Empty<string>();
    public string? DataProtectionKeyPath { get; set; }
    public string? DataProtectionCertificatePath { get; set; }
    public string? DataProtectionCertificatePassword { get; set; }
    public int MaxRequestBodyBytes { get; set; } = 4096;
    public int RequestsPerMinute { get; set; } = 240;
    public int MutationsPerMinute { get; set; } = 120;
    public int CreationsPerMinute { get; set; } = 20;
    public int GlobalRequestsPerMinute { get; set; } = 6000;
    public int MaxClientBuckets { get; set; } = 8192;
    public int MaxConcurrentRequests { get; set; } = 100;
}

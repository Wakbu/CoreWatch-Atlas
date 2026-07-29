namespace CoreWatch.Atlas.Server;

public sealed class ServerSecurityOptions
{
    public const string SectionName = "Atlas:Security";

    public bool RequireHttps { get; set; } = true;

    public bool AllowLoopbackHttp { get; set; } = true;

    public string DataProtectionKeyPath { get; set; } = "data/data-protection-keys";

    public int HstsMaxAgeDays { get; set; } = 365;
}

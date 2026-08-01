namespace CoreWatch.Atlas.Server;

public sealed class RegistrationOptions
{
    public const string SectionName = "Atlas:Registration";

    public int TokenLifetimeMinutes { get; set; } = 15;
}
// CoreWatch Atlas module: RegistrationOptions.

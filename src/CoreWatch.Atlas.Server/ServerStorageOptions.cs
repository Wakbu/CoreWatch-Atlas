namespace CoreWatch.Atlas.Server;

public sealed class ServerStorageOptions
{
    public const string SectionName = "Atlas:Server";

    public string DatabasePath { get; set; } = "data/corewatch-atlas.db";
}

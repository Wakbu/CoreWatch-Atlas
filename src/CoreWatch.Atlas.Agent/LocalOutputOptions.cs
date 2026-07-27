namespace CoreWatch.Atlas.Agent;

public sealed class LocalOutputOptions
{
    public const string SectionName = "Atlas:LocalOutput";

    public bool JsonEnabled { get; set; } = true;

    public PrometheusEndpointOptions Prometheus { get; set; } = new();
}

public sealed class PrometheusEndpointOptions
{
    public bool Enabled { get; set; }

    public string Url { get; set; } = "http://127.0.0.1:9464";
}

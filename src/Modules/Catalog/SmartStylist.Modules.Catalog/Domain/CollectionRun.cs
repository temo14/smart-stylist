namespace SmartStylist.Modules.Catalog.Domain;

public sealed class CollectionRun
{
    public long Id { get; set; }

    public int ShopId { get; set; }

    public Shop Shop { get; set; } = null!;

    public CollectorStrategy? Strategy { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Running;

    public int ProductsSeen { get; set; }

    public int SnapshotsWritten { get; set; }

    /// <summary>Pages or feed requests that failed or had no usable price.</summary>
    public int PagesFailed { get; set; }

    public string? Error { get; set; }
}

public enum RunStatus
{
    Running,
    Succeeded,
    Failed,
}

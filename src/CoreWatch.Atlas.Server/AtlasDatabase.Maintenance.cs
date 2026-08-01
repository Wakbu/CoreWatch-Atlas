namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    public async Task<IReadOnlyList<MaintenanceWindow>> ListMaintenanceWindowsAsync(CancellationToken ct=default)
    { await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT id,name,starts_at_utc,ends_at_utc FROM maintenance_windows WHERE ends_at_utc>=strftime('%Y-%m-%dT%H:%M:%fZ','now') ORDER BY starts_at_utc;";await using var r=await q.ExecuteReaderAsync(ct);var x=new List<MaintenanceWindow>();while(await r.ReadAsync(ct))x.Add(new(r.GetInt64(0),r.GetString(1),ParseTimestamp(r.GetString(2)),ParseTimestamp(r.GetString(3))));return x; }
    public async Task<MaintenanceWindow> CreateMaintenanceWindowAsync(MaintenanceWindowRequest x,CancellationToken ct=default)
    { if(string.IsNullOrWhiteSpace(x.Name)||x.Name.Length>80||x.EndsAtUtc<=x.StartsAtUtc||x.EndsAtUtc-x.StartsAtUtc>TimeSpan.FromDays(30))throw new ArgumentException("Invalid maintenance window.");await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="INSERT INTO maintenance_windows(name,starts_at_utc,ends_at_utc) VALUES($n,$s,$e);SELECT last_insert_rowid();";q.Parameters.AddWithValue("$n",x.Name.Trim());q.Parameters.AddWithValue("$s",FormatTimestamp(x.StartsAtUtc));q.Parameters.AddWithValue("$e",FormatTimestamp(x.EndsAtUtc));var id=Convert.ToInt64(await q.ExecuteScalarAsync(ct));return new(id,x.Name.Trim(),x.StartsAtUtc,x.EndsAtUtc); }
    public async Task<bool> IsMaintenanceActiveAsync(CancellationToken ct=default)
    { await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT EXISTS(SELECT 1 FROM maintenance_windows WHERE starts_at_utc<=strftime('%Y-%m-%dT%H:%M:%fZ','now') AND ends_at_utc>strftime('%Y-%m-%dT%H:%M:%fZ','now'));";return Convert.ToInt64(await q.ExecuteScalarAsync(ct))==1; }
}

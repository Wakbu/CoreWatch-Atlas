using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    public async Task<AssetMetadata?> GetAssetMetadataAsync(Guid id, CancellationToken ct = default)
    {
        await using var c = await OpenConnectionAsync(ct);
        await using var q = c.CreateCommand();
        q.CommandText = "SELECT owner,notes,role,ip_address FROM asset_metadata WHERE agent_id=$id;";
        q.Parameters.AddWithValue("$id", id.ToString("D"));
        string? owner, notes, role, ip;
        await using (var r = await q.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct)) return null;
            owner=AssetText(r,0); notes=AssetText(r,1); role=AssetText(r,2); ip=AssetText(r,3);
        }
        return new AssetMetadata(id, owner, notes, role, ip, await ListAgentTagsAsync(c, id, ct));
    }

    public async Task SetAssetMetadataAsync(Guid id, AssetMetadataRequest x, CancellationToken ct = default)
    {
        ValidateAsset(x);
        await using var c = await OpenConnectionAsync(ct);
        await using var tx = (SqliteTransaction)await c.BeginTransactionAsync(ct);
        await using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "INSERT INTO asset_metadata(agent_id,owner,notes,role,ip_address) VALUES($id,$owner,$notes,$role,$ip) ON CONFLICT(agent_id) DO UPDATE SET owner=excluded.owner,notes=excluded.notes,role=excluded.role,ip_address=excluded.ip_address;";
            q.Parameters.AddWithValue("$id", id.ToString("D"));
            q.Parameters.AddWithValue("$owner", Db(x.Owner)); q.Parameters.AddWithValue("$notes", Db(x.Notes));
            q.Parameters.AddWithValue("$role", Db(x.Role)); q.Parameters.AddWithValue("$ip", Db(x.IpAddress));
            await q.ExecuteNonQueryAsync(ct);
        }
        await using (var clear = c.CreateCommand()) { clear.Transaction = tx; clear.CommandText = "DELETE FROM agent_asset_tags WHERE agent_id=$id;"; clear.Parameters.AddWithValue("$id", id.ToString("D")); await clear.ExecuteNonQueryAsync(ct); }
        foreach (var tag in NormalizeTags(x.Tags))
        {
            await using var add = c.CreateCommand(); add.Transaction = tx;
            add.CommandText = "INSERT OR IGNORE INTO asset_tags(name) VALUES($name); INSERT INTO agent_asset_tags(agent_id,tag_id) SELECT $id,id FROM asset_tags WHERE name=$name COLLATE NOCASE;";
            add.Parameters.AddWithValue("$id", id.ToString("D")); add.Parameters.AddWithValue("$name", tag); await add.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<AssetTag>> ListAssetTagsAsync(CancellationToken ct = default)
    {
        await using var c = await OpenConnectionAsync(ct); await using var q = c.CreateCommand();
        q.CommandText = "SELECT id,name FROM asset_tags ORDER BY name;"; await using var r = await q.ExecuteReaderAsync(ct); var result = new List<AssetTag>();
        while (await r.ReadAsync(ct)) result.Add(new(r.GetInt64(0), r.GetString(1))); return result;
    }

    public async Task<bool> DeleteAssetTagAsync(long id, CancellationToken ct = default)
    { await using var c=await OpenConnectionAsync(ct); await using var q=c.CreateCommand(); q.CommandText="DELETE FROM asset_tags WHERE id=$id;"; q.Parameters.AddWithValue("$id",id); return await q.ExecuteNonQueryAsync(ct)==1; }

    private static async Task<IReadOnlyList<string>> ListAgentTagsAsync(SqliteConnection c, Guid id, CancellationToken ct)
    { await using var q=c.CreateCommand(); q.CommandText="SELECT t.name FROM asset_tags t JOIN agent_asset_tags x ON x.tag_id=t.id WHERE x.agent_id=$id ORDER BY t.name;"; q.Parameters.AddWithValue("$id",id.ToString("D")); await using var r=await q.ExecuteReaderAsync(ct); var tags=new List<string>(); while(await r.ReadAsync(ct))tags.Add(r.GetString(0)); return tags; }
    private static string? AssetText(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static object Db(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value.Trim();
    private static IEnumerable<string> NormalizeTags(IReadOnlyList<string>? tags)=>(tags??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(20);
    private static void ValidateAsset(AssetMetadataRequest x)
    { if(x.Owner?.Length>100||x.Notes?.Length>2000||x.Role?.Length>80||x.IpAddress?.Length>64||(x.Tags?.Any(t=>t.Length>48)??false))throw new ArgumentException("Invalid asset metadata."); }
}
// CoreWatch Atlas module: AtlasDatabase.Assets.

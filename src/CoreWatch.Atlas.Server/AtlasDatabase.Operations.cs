using Microsoft.Data.Sqlite;

namespace CoreWatch.Atlas.Server;

public sealed partial class AtlasDatabase
{
    public async Task<IReadOnlyList<ServerGroup>> ListServerGroupsAsync(CancellationToken ct = default)
    {
        await using var c = await OpenConnectionAsync(ct); await using var q = c.CreateCommand();
        q.CommandText = "SELECT g.id,g.name,g.description,COUNT(m.agent_id) FROM server_groups g LEFT JOIN server_group_members m ON m.group_id=g.id GROUP BY g.id ORDER BY g.name;";
        await using var r = await q.ExecuteReaderAsync(ct); var result = new List<ServerGroup>();
        while (await r.ReadAsync(ct)) result.Add(new(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3))); return result;
    }
    public async Task<ServerGroup> CreateServerGroupAsync(ServerGroupRequest x, CancellationToken ct = default)
    {
        ValidateGroup(x); await using var c = await OpenConnectionAsync(ct); await using var q = c.CreateCommand();
        q.CommandText = "INSERT INTO server_groups(name,description) VALUES($name,$description); SELECT last_insert_rowid();"; q.Parameters.AddWithValue("$name", x.Name.Trim()); q.Parameters.AddWithValue("$description", (object?)x.Description?.Trim() ?? DBNull.Value);
        var id = Convert.ToInt64(await q.ExecuteScalarAsync(ct)); return new(id, x.Name.Trim(), x.Description?.Trim(), 0);
    }
    public async Task<bool> DeleteServerGroupAsync(long id, CancellationToken ct = default)
    { await using var c = await OpenConnectionAsync(ct); await using var q = c.CreateCommand(); q.CommandText="DELETE FROM server_groups WHERE id=$id;";q.Parameters.AddWithValue("$id",id);return await q.ExecuteNonQueryAsync(ct)==1; }
    public async Task<bool> SetAgentGroupAsync(Guid agentId, long groupId, bool member, CancellationToken ct = default)
    {
        // 중복 추가는 정상적인 멱등 요청으로 취급한다. 화면 재시도나 API 재전송 때문에
        // 같은 Agent가 두 번 연결되는 일을 막기 위해 복합 기본 키와 함께 사용한다.
        await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText=member?"INSERT OR IGNORE INTO server_group_members(group_id,agent_id) VALUES($group,$agent);":"DELETE FROM server_group_members WHERE group_id=$group AND agent_id=$agent;";q.Parameters.AddWithValue("$group",groupId);q.Parameters.AddWithValue("$agent",agentId.ToString("D"));return await q.ExecuteNonQueryAsync(ct)>0;
    }
    public async Task<IReadOnlyList<Guid>> ListGroupAgentIdsAsync(long groupId,CancellationToken ct=default)
    { await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT agent_id FROM server_group_members WHERE group_id=$id ORDER BY agent_id;";q.Parameters.AddWithValue("$id",groupId);await using var r=await q.ExecuteReaderAsync(ct);var result=new List<Guid>();while(await r.ReadAsync(ct))result.Add(Guid.Parse(r.GetString(0)));return result; }
    public async Task<IReadOnlyList<AssetInventory>> ListAssetInventoryAsync(CancellationToken ct=default)
    { await using var c=await OpenConnectionAsync(ct);await using var q=c.CreateCommand();q.CommandText="SELECT a.agent_id,a.host_name,a.operating_system,a.architecture,a.agent_version,m.ip_address,m.role,m.owner FROM agents a LEFT JOIN asset_metadata m ON m.agent_id=a.agent_id WHERE a.archived_at_utc IS NULL ORDER BY a.host_name;";var raw=new List<(Guid,string,string,string,string,string?,string?,string?)>();await using(var r=await q.ExecuteReaderAsync(ct)){while(await r.ReadAsync(ct))raw.Add((Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetString(7)));}var result=new List<AssetInventory>();foreach(var x in raw)result.Add(new(x.Item1,x.Item2,x.Item3,x.Item4,x.Item5,x.Item6,x.Item7,x.Item8,await ListInventoryTagsAsync(c,x.Item1,ct)));return result; }
    private static async Task<IReadOnlyList<string>> ListInventoryTagsAsync(SqliteConnection c,Guid id,CancellationToken ct){await using var q=c.CreateCommand();q.CommandText="SELECT t.name FROM asset_tags t JOIN agent_asset_tags x ON x.tag_id=t.id WHERE x.agent_id=$id ORDER BY t.name;";q.Parameters.AddWithValue("$id",id.ToString("D"));await using var r=await q.ExecuteReaderAsync(ct);var result=new List<string>();while(await r.ReadAsync(ct))result.Add(r.GetString(0));return result;}
    private static void ValidateGroup(ServerGroupRequest x)
    { if(string.IsNullOrWhiteSpace(x.Name)||x.Name.Length>64||x.Description?.Length>240) throw new ArgumentException("Invalid server group."); }
}

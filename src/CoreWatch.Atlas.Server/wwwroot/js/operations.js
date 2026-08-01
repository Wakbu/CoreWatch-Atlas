// 보고서 화면은 원본 Snapshot 전체를 내려받지 않고 서버가 계산한 집계 결과만 사용한다.
// 수집 데이터가 많은 환경에서도 브라우저의 메모리 사용량을 일정하게 유지하기 위한 분리다.
(() => {
  "use strict";
  const content = document.querySelector("#content");
  const title = document.querySelector("#title");
  const api = path => fetch(path, { headers: { Accept: "application/json" } });
  const percent = value => value == null ? "-" : `${Number(value).toFixed(1)}%`;
  async function renderReports() {
    const response = await api("/api/v1/agents");
    if (!response.ok) return;
    const agents = await response.json();
    title.textContent = "Server reports";
    content.innerHTML = `<section class="panel"><h2>Server reports</h2><p class="subtitle">7-day availability, resource use, and alert summary.</p><div class="list">${agents.map(a => `<div class="row"><span><strong>${a.hostName}</strong><small>${a.operatingSystem}</small></span><button class="secondary" data-report="${a.agentId}">View report</button></div>`).join("")}</div><section id="reportOutput"></section></section>`;
    content.querySelectorAll("[data-report]").forEach(button => button.onclick = async () => {
      const report = await (await api(`/api/v1/agents/${button.dataset.report}/report?days=7`)).json();
      document.querySelector("#reportOutput").innerHTML = `<div class="panel"><h3>${report.hostName} · 7 days</h3><div class="summary"><div><span>Availability</span><strong>${percent(report.availabilityPercent)}</strong></div><div><span>CPU avg / max</span><strong>${percent(report.cpu.average)} / ${percent(report.cpu.maximum)}</strong></div><div><span>Memory avg / max</span><strong>${percent(report.memory.average)} / ${percent(report.memory.maximum)}</strong></div><div><span>Disk latest</span><strong>${percent(report.disk.latest)}</strong></div></div><p class="subtitle">Snapshots ${report.snapshotCount} · Alerts ${report.alerts.length}</p></div>`;
    });
  }
  async function csrf() { return (await (await api("/api/v1/auth/csrf")).json()).token; }
  async function write(path, method, body) {
    const response = await fetch(path, { method, headers: { "Content-Type": "application/json", "X-CoreWatch-CSRF": await csrf() }, body: body && JSON.stringify(body) });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
  }
  async function renderGroups() {
    const groups = await (await api("/api/v1/server-groups")).json();
    title.textContent = "Server groups";
    content.innerHTML = `<section class="panel"><h2>Server groups</h2><p class="subtitle">Group servers by environment, team, or service role.</p><div class="list">${groups.map(g => `<div class="row"><span><strong>${g.name}</strong><small>${g.description || "No description"} · ${g.memberCount} servers</small></span><button class="danger-button" data-delete-group="${g.id}">Delete</button></div>`).join("")}</div><form id="groupForm" class="settings-form"><input name="name" placeholder="Production DB" required><input name="description" placeholder="Description"><button class="primary">Create group</button></form></section>`;
    content.querySelectorAll("[data-delete-group]").forEach(b => b.onclick = async () => { await write(`/api/v1/server-groups/${b.dataset.deleteGroup}`, "DELETE"); renderGroups(); });
    content.querySelector("#groupForm").onsubmit = async event => { event.preventDefault(); const f = new FormData(event.currentTarget); await write("/api/v1/server-groups", "POST", { name: f.get("name"), description: f.get("description") || null }); renderGroups(); };
  }
  async function renderMaintenance() {
    const windows = await (await api("/api/v1/maintenance-windows")).json();
    title.textContent = "Maintenance windows";
    content.innerHTML = `<section class="panel"><h2>Maintenance windows</h2><p class="subtitle">Alerts continue to be recorded; webhook delivery is paused during these periods.</p><div class="list">${windows.map(w => `<div class="row"><span><strong>${w.name}</strong><small>${new Date(w.startsAtUtc).toLocaleString()} – ${new Date(w.endsAtUtc).toLocaleString()}</small></span></div>`).join("")}</div><form id="maintenanceForm" class="settings-form"><input name="name" placeholder="Database patch" required><input name="start" type="datetime-local" required><input name="end" type="datetime-local" required><button class="primary">Schedule</button></form></section>`;
    content.querySelector("#maintenanceForm").onsubmit = async event => { event.preventDefault(); const f = new FormData(event.currentTarget); await write("/api/v1/maintenance-windows", "POST", { name: f.get("name"), startsAtUtc: new Date(f.get("start")).toISOString(), endsAtUtc: new Date(f.get("end")).toISOString() }); renderMaintenance(); };
  }
  function route() { if (location.hash === "#/reports") renderReports().catch(console.error); if (location.hash === "#/groups") renderGroups().catch(console.error); if (location.hash === "#/maintenance") renderMaintenance().catch(console.error); }
  addEventListener("hashchange", () => setTimeout(route, 0));
  document.addEventListener("atlas:render", () => setTimeout(route, 0));
  setTimeout(route, 250);
})();

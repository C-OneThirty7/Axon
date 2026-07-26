const state = {
  session: null,
  status: null,
  users: [],
  rooms: [],
  selectedRoom: null,
  lastBatch: [],
  searchTimer: null
};
const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];

async function api(path, options = {}) {
  const response = await fetch(path, {
    credentials: "same-origin",
    headers: options.body ? { "Content-Type": "application/json", ...(options.headers || {}) } : options.headers,
    ...options
  });
  const type = response.headers.get("content-type") || "";
  const payload = type.includes("json") ? await response.json() : await response.text();
  if (!response.ok) {
    if (response.status === 401 && path !== "/api/session") showLogin();
    throw new Error(payload?.error || `Request failed (${response.status})`);
  }
  return payload;
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (char) => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;"
  })[char]);
}

function showLogin() {
  $("#shell").hidden = true;
  $("#login").hidden = false;
  closeDrawers();
}

function showShell() {
  $("#login").hidden = true;
  $("#shell").hidden = false;
  $("#operator").textContent = state.session?.userId || "Operator";
}

function toast(message, duration = 3500) {
  const element = $("#toast");
  element.textContent = message;
  element.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => { element.hidden = true; }, duration);
}

function selectView(name) {
  const titles = {
    overview: "System overview",
    users: "User directory",
    rooms: "Room control",
    batch: "Batch provisioning",
    logs: "Service logs"
  };
  $$(".nav-item").forEach((item) => item.classList.toggle("active", item.dataset.view === name));
  $$(".view").forEach((view) => view.classList.toggle("active", view.id === `view-${name}`));
  $("#view-title").textContent = titles[name];
  if (name === "overview") loadStatus();
  if (name === "users") loadUsers();
  if (name === "rooms") loadRooms();
  if (name === "logs") loadLogs();
}

function parseJsonLines(raw) {
  const value = (raw || "").trim();
  if (!value) return [];
  try {
    if (value.startsWith("[")) {
      const parsed = JSON.parse(value);
      return Array.isArray(parsed) ? parsed : [parsed];
    }
    return value.split(/\r?\n/).filter(Boolean).map((line) => JSON.parse(line));
  } catch {
    return [];
  }
}

function activityFor(timestamp) {
  if (!timestamp) return { label: "Never seen", className: "quiet", recent: false };
  const age = Date.now() - timestamp;
  if (age < 15 * 60 * 1000) return { label: "Recently active", className: "live", recent: true };
  if (age < 60 * 60 * 1000) return { label: "Active this hour", className: "warm", recent: false };
  if (age < 24 * 60 * 60 * 1000) return { label: "Active today", className: "quiet", recent: false };
  return { label: "Not recent", className: "quiet", recent: false };
}

async function loadStatus() {
  try {
    const status = await api("/api/status");
    state.status = status;
    const healthy = status.synapse.healthy && status.docker.healthy;
    $("#health-dot").className = healthy ? "good" : "bad";
    $("#health-label").textContent = healthy ? "All services healthy" : "Attention required";
    $("#synapse-state").textContent = status.synapse.healthy ? "Healthy" : "Unavailable";

    const services = parseJsonLines(status.docker.output);
    const stats = parseJsonLines(status.docker.stats);
    const labels = { postgres: "PostgreSQL", synapse: "Synapse", gateway: "nginx gateway" };
    const containers = {
      postgres: "axon-postgres-1",
      synapse: "axon-synapse-1",
      gateway: "axon-gateway-1"
    };
    $("#services").innerHTML = ["postgres", "synapse", "gateway"].map((name) => {
      const service = services.find((entry) => entry.Service === name);
      const usage = stats.find((entry) =>
        entry.Name === containers[name] || entry.Container === containers[name]);
      const running = String(service?.State || "").toLowerCase() === "running";
      const detail = service
        ? `${service.State || "unknown"} · ${service.Health || (running ? "health pending" : "stopped")}`
        : "Container not created";
      const resource = usage
        ? `${usage.CPUPerc || usage.CPU || "-"} CPU · ${usage.MemUsage || usage.Memory || "-"}`
        : "-";
      return `<div>
        <span>${labels[name]}</span>
        <em>${escapeHtml(detail)}</em>
        <small>${escapeHtml(resource)}</small>
        <div class="service-actions">
          <button class="secondary" data-service-action="${running ? "stop" : "start"}" data-service="${name}">${running ? "Stop" : "Start"}</button>
          <button class="secondary" data-service-action="restart" data-service="${name}" ${running ? "" : "disabled"}>Restart</button>
        </div>
      </div>`;
    }).join("");
  } catch (error) {
    $("#health-dot").className = "bad";
    $("#health-label").textContent = error.message;
  }
}

async function loadUsers() {
  const search = encodeURIComponent($("#user-search").value.trim());
  $("#user-rows").innerHTML = '<tr><td colspan="6" class="empty">Loading accounts...</td></tr>';
  try {
    const page = await api(`/api/users?limit=200&search=${search}`);
    state.users = page.users;
    $("#account-total").textContent = page.total;
    $("#recent-total").textContent = page.users.filter((user) =>
      activityFor(user.lastSeenTimestamp).recent).length;
    if (!page.users.length) {
      $("#user-rows").innerHTML = '<tr><td colspan="6" class="empty">No matching accounts</td></tr>';
      return;
    }
    $("#user-rows").innerHTML = page.users.map((user) => {
      const localpart = user.name.slice(1).split(":")[0];
      const lastSeen = user.lastSeenTimestamp ? new Date(user.lastSeenTimestamp).toLocaleString() : "Never";
      const accountState = user.deactivated ? "Deactivated" : user.locked ? "Locked" : "Active";
      const activity = activityFor(user.lastSeenTimestamp);
      const isCurrentOperator = user.name === state.session?.userId;
      return `<tr>
        <td>${escapeHtml(user.name)}<small>${escapeHtml(user.displayName || "No display name")}</small></td>
        <td><span class="tag ${user.admin ? "admin" : ""}">${user.admin ? "Admin" : "User"}</span></td>
        <td>${accountState}</td>
        <td><span class="activity ${activity.className}"><i></i>${activity.label}</span></td>
        <td>${escapeHtml(lastSeen)}</td>
        <td><div class="row-actions">
          <button data-action="password" data-user="${escapeHtml(localpart)}">Reset password</button>
          ${isCurrentOperator ? '<button disabled>Current admin</button>' : `<button data-action="role" data-user="${escapeHtml(localpart)}" data-admin="${user.admin}">${user.admin ? "Demote" : "Make admin"}</button>`}
          ${isCurrentOperator ? "" : `<button data-action="lock" data-user="${escapeHtml(localpart)}" data-locked="${user.locked}">${user.locked ? "Unlock" : "Lock"}</button>`}
        </div></td>
      </tr>`;
    }).join("");
  } catch (error) {
    $("#user-rows").innerHTML = `<tr><td colspan="6" class="empty">${escapeHtml(error.message)}</td></tr>`;
  }
}

async function loadRooms() {
  const search = encodeURIComponent($("#room-search").value.trim());
  $("#room-rows").innerHTML = '<tr><td colspan="6" class="empty">Loading rooms...</td></tr>';
  try {
    const page = await api(`/api/rooms?limit=200&search=${search}`);
    state.rooms = page.rooms;
    $("#room-total").textContent = page.total;
    if (!page.rooms.length) {
      $("#room-rows").innerHTML = '<tr><td colspan="6" class="empty">No matching rooms</td></tr>';
      return;
    }
    $("#room-rows").innerHTML = page.rooms.map((room) => `<tr>
      <td>${escapeHtml(room.name || "Unnamed room")}<small>${escapeHtml(room.canonicalAlias || room.roomId)}</small></td>
      <td>${room.joinedMembers}<small>${room.joinedLocalMembers} local</small></td>
      <td><span class="tag ${room.encrypted ? "encrypted" : "warning"}">${room.encrypted ? "E2EE" : "Unencrypted"}</span></td>
      <td>${escapeHtml(room.joinRules)}</td>
      <td>${escapeHtml(room.creator || "Unknown")}</td>
      <td><button class="secondary room-open" data-room-id="${escapeHtml(room.roomId)}">Manage</button></td>
    </tr>`).join("");
  } catch (error) {
    $("#room-rows").innerHTML = `<tr><td colspan="6" class="empty">${escapeHtml(error.message)}</td></tr>`;
  }
}

async function loadRoomMembers() {
  const room = state.selectedRoom;
  if (!room) return;
  $("#room-members").innerHTML = '<div class="empty">Loading members...</div>';
  try {
    const result = await api(`/api/rooms/${encodeURIComponent(room.roomId)}/members`);
    if (!result.members.length) {
      $("#room-members").innerHTML = '<div class="empty">No joined members</div>';
      return;
    }
    $("#room-members").innerHTML = result.members.map((userId) => {
      const current = userId === state.session?.userId;
      return `<div>
        <span>${escapeHtml(userId)}</span>
        ${current
          ? '<small>Current operator</small>'
          : `<button class="danger subtle" data-remove-member="${escapeHtml(userId)}">Remove</button>`}
      </div>`;
    }).join("");
  } catch (error) {
    $("#room-members").innerHTML = `<div class="empty">${escapeHtml(error.message)}</div>`;
  }
}

function openDrawer(id) {
  closeDrawers();
  $("#drawer-backdrop").hidden = false;
  $(id).hidden = false;
}

function closeDrawers() {
  $("#drawer-backdrop").hidden = true;
  $$(".drawer").forEach((drawer) => { drawer.hidden = true; });
}

function openRoom(roomId) {
  const room = state.rooms.find((entry) => entry.roomId === roomId);
  if (!room) return;
  state.selectedRoom = room;
  $("#room-drawer-name").textContent = room.name || "Unnamed room";
  $("#room-drawer-id").textContent = room.roomId;
  $("#room-facts").innerHTML = `
    <div><dt>Members</dt><dd>${room.joinedMembers} total / ${room.joinedLocalMembers} local</dd></div>
    <div><dt>Protection</dt><dd>${room.encrypted ? "End-to-end encrypted" : "Not encrypted"}</dd></div>
    <div><dt>Join rule</dt><dd>${escapeHtml(room.joinRules)}</dd></div>
    <div><dt>Creator</dt><dd>${escapeHtml(room.creator || "Unknown")}</dd></div>`;
  openDrawer("#room-drawer");
  loadRoomMembers();
}

async function loadLogs() {
  $("#logs").textContent = "Loading bounded logs...";
  try { $("#logs").textContent = await api("/api/logs"); }
  catch (error) { $("#logs").textContent = error.message; }
}

function updateBatchPreview() {
  const form = new FormData($("#batch-form"));
  const prefix = form.get("prefix") || "user";
  const start = Number(form.get("start") || 1);
  const count = Math.min(8, Number(form.get("count") || 1));
  const padding = Number(form.get("padding") || 0);
  $("#batch-preview").textContent = [...Array(count)].map((_, index) =>
    `${prefix}${String(start + index).padStart(padding, "0")}`
  ).join("\n") + (Number(form.get("count")) > 8 ? "\n…" : "");
}

$("#login-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  $("#login-error").textContent = "";
  const data = new FormData(form);
  try {
    const session = await api("/api/session", {
      method: "POST",
      body: JSON.stringify({ username: data.get("username"), password: data.get("password") })
    });
    state.session = session;
    form.reset();
    showShell();
    await Promise.all([loadStatus(), loadUsers(), loadRooms()]);
  } catch (error) {
    $("#login-error").textContent = error.message;
  }
});

$("#logout").addEventListener("click", async () => {
  await api("/api/session", { method: "DELETE" });
  state.session = null;
  showLogin();
});

$$(".nav-item").forEach((button) =>
  button.addEventListener("click", () => selectView(button.dataset.view)));
$("#refresh-status").addEventListener("click", loadStatus);
$("#refresh-users").addEventListener("click", loadUsers);
$("#refresh-rooms").addEventListener("click", loadRooms);
$("#refresh-members").addEventListener("click", loadRoomMembers);
$("#refresh-logs").addEventListener("click", loadLogs);
$("#user-search").addEventListener("input", () => {
  clearTimeout(state.searchTimer);
  state.searchTimer = setTimeout(loadUsers, 250);
});
$("#room-search").addEventListener("input", () => {
  clearTimeout(state.searchTimer);
  state.searchTimer = setTimeout(loadRooms, 250);
});
$("#open-create-user").addEventListener("click", () => {
  $("#user-form").reset();
  $("#user-error").textContent = "";
  openDrawer("#user-drawer");
  $("#user-drawer input[name=username]").focus();
});
$("#open-create-room").addEventListener("click", () => {
  $("#room-form").reset();
  $("#room-error").textContent = "";
  openDrawer("#room-create-drawer");
  $("#room-create-drawer input[name=name]").focus();
});
$$("[data-close-drawer]").forEach((button) => button.addEventListener("click", closeDrawers));
$("#drawer-backdrop").addEventListener("click", closeDrawers);

$("#user-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  $("#user-error").textContent = "";
  try {
    await api("/api/users", {
      method: "POST",
      body: JSON.stringify({
        username: data.get("username"),
        displayName: data.get("displayName") || null,
        password: data.get("password"),
        admin: data.get("admin") === "on"
      })
    });
    closeDrawers();
    toast(`Created @${data.get("username")}:axon.home.arpa`);
    await loadUsers();
  } catch (error) {
    $("#user-error").textContent = error.message;
  }
});

$("#room-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  const invite = String(data.get("invite") || "").split(",")
    .map((value) => value.trim())
    .filter(Boolean)
    .map((value) => value.startsWith("@") ? value : `@${value}:axon.home.arpa`);
  $("#room-error").textContent = "";
  try {
    const result = await api("/api/rooms", {
      method: "POST",
      body: JSON.stringify({
        name: data.get("name"),
        topic: data.get("topic") || null,
        invite
      })
    });
    closeDrawers();
    toast(`Created encrypted room ${result.value}`);
    await loadRooms();
  } catch (error) {
    $("#room-error").textContent = error.message;
  }
});

$("#room-member-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  const userId = String(data.get("userId")).trim();
  const room = state.selectedRoom;
  if (!room) return;
  if (!confirm(`Take room control if required, then add ${userId} to ${room.name}? The administrator may visibly join the room.`)) return;
  try {
    await api(`/api/rooms/${encodeURIComponent(room.roomId)}/members`, {
      method: "POST",
      body: JSON.stringify({ userId })
    });
    event.currentTarget.reset();
    toast(`${userId} added to ${room.name}`);
    await Promise.all([loadRoomMembers(), loadRooms()]);
  } catch (error) {
    toast(error.message, 6000);
  }
});

$("#room-rows").addEventListener("click", (event) => {
  const button = event.target.closest(".room-open");
  if (button) openRoom(button.dataset.roomId);
});

$("#room-members").addEventListener("click", async (event) => {
  const button = event.target.closest("[data-remove-member]");
  const room = state.selectedRoom;
  if (!button || !room) return;
  const userId = button.dataset.removeMember;
  if (!confirm(`Take room control and remove ${userId} from ${room.name}? This creates a visible membership event.`)) return;
  try {
    await api(`/api/rooms/${encodeURIComponent(room.roomId)}/members/${encodeURIComponent(userId)}`, {
      method: "DELETE"
    });
    toast(`${userId} removed from ${room.name}`);
    await Promise.all([loadRoomMembers(), loadRooms()]);
  } catch (error) {
    toast(error.message, 6000);
  }
});

$("#delete-room").addEventListener("click", async () => {
  const room = state.selectedRoom;
  if (!room) return;
  const confirmation = prompt(`Permanently delete, block, and purge this room?\n\nType "${room.name}" to confirm.`);
  if (confirmation !== room.name) {
    if (confirmation !== null) toast("Room deletion cancelled: confirmation did not match.");
    return;
  }
  try {
    await api(`/api/rooms/${encodeURIComponent(room.roomId)}`, { method: "DELETE" });
    closeDrawers();
    toast(`Deletion started for ${room.name}`, 6000);
    setTimeout(loadRooms, 1500);
  } catch (error) {
    toast(error.message, 6000);
  }
});

$("#batch-form").addEventListener("input", updateBatchPreview);
$("#batch-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const data = new FormData(event.currentTarget);
  const count = Number(data.get("count"));
  if (!confirm(`Create ${count} Matrix accounts with the displayed stock password?`)) return;
  $("#batch-result").innerHTML = "<div>Creating accounts...</div>";
  try {
    const result = await api("/api/users/batch", {
      method: "POST",
      body: JSON.stringify({
        prefix: data.get("prefix"),
        start: Number(data.get("start")),
        count,
        padding: Number(data.get("padding")),
        password: data.get("password"),
        admin: data.get("admin") === "on"
      })
    });
    $("#batch-result").innerHTML = result.results.map((entry) =>
      `<div class="${entry.success ? "success" : "failure"}">${escapeHtml(entry.username)} — ${entry.success ? "created" : escapeHtml(entry.error)}</div>`
    ).join("");
    state.lastBatch = result.results.filter((entry) => entry.success).map((entry) => ({
      username: entry.username,
      matrixId: `@${entry.username}:axon.home.arpa`,
      password: data.get("password"),
      admin: data.get("admin") === "on"
    }));
    $("#download-batch").hidden = state.lastBatch.length === 0;
    toast(`Created ${result.created} of ${result.requested} accounts`);
    await loadUsers();
  } catch (error) {
    $("#batch-result").innerHTML = `<div class="failure">${escapeHtml(error.message)}</div>`;
  }
});

$("#download-batch").addEventListener("click", () => {
  if (!state.lastBatch.length) return;
  const quote = (value) => `"${String(value).replaceAll('"', '""')}"`;
  const rows = [
    ["username", "matrix_id", "stock_password", "server_admin"],
    ...state.lastBatch.map((item) => [item.username, item.matrixId, item.password, item.admin])
  ];
  const csv = rows.map((row) => row.map(quote).join(",")).join("\r\n");
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = `axon-issued-users-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
});

$("#user-rows").addEventListener("click", async (event) => {
  const button = event.target.closest("button[data-action]");
  if (!button) return;
  const username = button.dataset.user;
  try {
    if (button.dataset.action === "password") {
      const password = prompt(`New password for ${username} (6-256 characters):`);
      if (!password) return;
      if (!confirm("Reset this password and sign out all existing devices?")) return;
      await api(`/api/users/${encodeURIComponent(username)}`, {
        method: "PUT",
        body: JSON.stringify({ password, logoutDevices: true })
      });
      toast(`Password reset for ${username}`);
    }
    if (button.dataset.action === "role") {
      const admin = button.dataset.admin !== "true";
      if (!confirm(`${admin ? "Promote" : "Demote"} ${username}?`)) return;
      await api(`/api/users/${encodeURIComponent(username)}`, {
        method: "PUT",
        body: JSON.stringify({ admin })
      });
      toast(`${username} is now ${admin ? "an administrator" : "a standard user"}`);
    }
    if (button.dataset.action === "lock") {
      const locked = button.dataset.locked !== "true";
      await api(`/api/users/${encodeURIComponent(username)}`, {
        method: "PUT",
        body: JSON.stringify({ locked })
      });
      toast(`${username} ${locked ? "locked" : "unlocked"}`);
    }
    await loadUsers();
  } catch (error) {
    toast(error.message);
  }
});

$("#services").addEventListener("click", async (event) => {
  const button = event.target.closest("[data-service-action]");
  if (!button) return;
  const { service, serviceAction: action } = button.dataset;
  const warning = service === "postgres" && action !== "start"
    ? " This will interrupt Synapse until the database and Synapse recover."
    : "";
  if (!confirm(`${action[0].toUpperCase()}${action.slice(1)} ${service}?${warning}`)) return;
  try {
    await api("/api/services/action", {
      method: "POST",
      body: JSON.stringify({ service, action })
    });
    toast(`${service}: ${action} requested`);
    setTimeout(loadStatus, action === "stop" ? 700 : 1800);
  } catch (error) {
    toast(error.message, 6000);
  }
});

$$("[data-stack-action]").forEach((button) => button.addEventListener("click", async () => {
  const action = button.dataset.stackAction;
  const warning = action === "stop"
    ? "All Matrix client access and database services will pause until Start all is selected."
    : `Apply ${action} to the complete Axon stack?`;
  if (!confirm(warning)) return;
  try {
    await api("/api/stack/action", {
      method: "POST",
      body: JSON.stringify({ action })
    });
    toast(`Stack ${action} requested`);
    setTimeout(loadStatus, action === "stop" ? 700 : 2200);
  } catch (error) {
    toast(error.message, 6000);
  }
}));

(async function boot() {
  updateBatchPreview();
  try {
    const session = await api("/api/session");
    if (!session.authenticated) return showLogin();
    state.session = session;
    showShell();
    await Promise.all([loadStatus(), loadUsers(), loadRooms()]);
  } catch {
    showLogin();
  }
})();

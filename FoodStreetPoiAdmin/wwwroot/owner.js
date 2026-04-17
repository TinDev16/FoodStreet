(() => {
  const $ = (s) => document.querySelector(s);

  const authDialogEl = $("#authDialog");
  const authFormEl = $("#authForm");
  const authStatusEl = $("#authStatus");
  const logoutBtn = $("#logoutBtn");
  const sidebarUserNameEl = $("#sidebarUserName");
  const sidebarUserRoleEl = $("#sidebarUserRole");
  const sidebarUserAvatarEl = $("#sidebarUserAvatar");
  const ownerManageNavEl = $("#ownerManageNav");
  const userManageNavEl = $("#userManageNav");
  const qrManageNavEl = $("#qrManageNav");
  const monitoringNavEl = $("#monitoringNav");

  const ownerFormEl = $("#ownerForm");
  const ownerResetBtn = $("#ownerResetBtn");
  const ownerStatusEl = $("#ownerStatus");
  const ownerRowsEl = $("#ownerRows");
  const ownerHistoryRowsEl = $("#ownerHistoryRows");
  const ownerActiveTableEl = $("#ownerActiveTable");
  const ownerHistoryTableEl = $("#ownerHistoryTable");
  const ownerTabActiveBtn = $("#ownerTabActiveBtn");
  const ownerTabHistoryBtn = $("#ownerTabHistoryBtn");
  const poiAssignRowsEl = $("#poiAssignRows");
  const assignOwnerIdEl = $("#assignOwnerId");
  const poiAssignStatusEl = $("#poiAssignStatus");
  const ownerPoiDialogEl = $("#ownerPoiDialog");
  const ownerPoiTabsHeaderEl = $("#ownerPoiTabsHeader");
  const ownerPoiTabsRowsEl = $("#ownerPoiTabsRows");
  const ownerPoiTabsCloseBtn = $("#ownerPoiTabsCloseBtn");

  const state = {
    token: (localStorage.getItem("adminToken") || "").trim(),
    me: null,
    owners: [],
    ownerHistory: [],
    pois: [],
    ownerTab: "active",
    ownerPoiTabs: [],
    ownerPoiActiveTabId: "",
  };

  const safeError = async (res) => {
    try {
      const j = await res.json();
      return j?.error ? `${j.error}${j.detail ? `: ${j.detail}` : ""}` : JSON.stringify(j);
    } catch {
      return `${res.status} ${res.statusText}`;
    }
  };

  const headers = (extra = {}) => {
    const h = { Accept: "application/json", ...extra };
    if (state.token) h.Authorization = `Bearer ${state.token}`;
    return h;
  };

  const apiGet = async (url) => {
    const res = await fetch(url, { headers: headers() });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const apiPostJson = async (url, body) => {
    const res = await fetch(url, {
      method: "POST",
      headers: headers({ "Content-Type": "application/json" }),
      body: JSON.stringify(body),
    });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const apiPutJson = async (url, body) => {
    const res = await fetch(url, {
      method: "PUT",
      headers: headers({ "Content-Type": "application/json" }),
      body: JSON.stringify(body),
    });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const apiDelete = async (url) => {
    const res = await fetch(url, { method: "DELETE", headers: headers() });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json().catch(() => ({}));
  };

  const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

  const updateIdentityUi = () => {
    const username = state.me?.username || "Guest";
    const role = state.me?.role || "";
    const roleCode = role.toLowerCase();
    const canManageUsers = !!roleCode && roleCode !== "owner";
    const fullName = state.me?.fullName || username;
    sidebarUserNameEl.textContent = fullName;
    sidebarUserRoleEl.textContent = role ? role.toUpperCase() : "Chua dang nhap";
    sidebarUserAvatarEl.src = `https://ui-avatars.com/api/?name=${encodeURIComponent(fullName)}&background=4f46e5&color=fff&rounded=true`;
    if (ownerManageNavEl) {
      ownerManageNavEl.hidden = roleCode === "owner";
    }
    if (userManageNavEl) {
      userManageNavEl.hidden = !canManageUsers;
    }
    if (qrManageNavEl) {
      qrManageNavEl.hidden = !roleCode;
    }
    if (monitoringNavEl) {
      monitoringNavEl.hidden = !(roleCode === "superadmin" || roleCode === "owner");
    }
  };

  const ensureSuperAdmin = () => {
    if ((state.me?.role || "").toLowerCase() !== "superadmin") {
      ownerStatusEl.textContent = "Ban khong co quyen quan ly owner.";
      ownerRowsEl.innerHTML = `<tr><td colspan="4" class="muted">Khong co quyen truy cap.</td></tr>`;
      poiAssignRowsEl.innerHTML = `<tr><td colspan="5" class="muted">Khong co quyen truy cap.</td></tr>`;
      return false;
    }
    return true;
  };

  const resetOwnerForm = () => {
    $("#ownerId").value = "";
    $("#ownerUsername").value = "";
    $("#ownerFullName").value = "";
    $("#ownerPassword").value = "";
    ownerStatusEl.textContent = "";
  };

  const renderOwnerSelect = () => {
    assignOwnerIdEl.innerHTML = "";
    const unassign = document.createElement("option");
    unassign.value = "";
    unassign.textContent = "— Bỏ gán owner —";
    assignOwnerIdEl.appendChild(unassign);
    for (const o of state.owners.filter((x) => !x.isDeleted)) {
      const opt = document.createElement("option");
      opt.value = o.id;
      opt.textContent = o.fullName ? `${o.username} (${o.fullName})` : o.username;
      assignOwnerIdEl.appendChild(opt);
    }
  };

  const renderOwners = () => {
    ownerRowsEl.innerHTML = "";
    ownerHistoryRowsEl.innerHTML = "";

    const activeOwners = state.owners.filter((x) => !x.isDeleted);
    const deletedOwners = state.ownerHistory.filter((x) => !!x.isDeleted);

    if (!activeOwners.length) {
      ownerRowsEl.innerHTML = `<tr><td colspan="4" class="muted">Chua co owner.</td></tr>`;
    }
    for (const o of activeOwners) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${esc(o.id)}</td>
        <td>${esc(o.username)}</td>
        <td>${esc(o.fullName || "")}</td>
        <td>
          <button type="button" class="secondary icon-only" title="Xem POI đã gán" data-action="view-owner-pois" data-id="${esc(o.id)}"><i class="fa-regular fa-eye"></i></button>
          <button type="button" class="secondary icon-only" title="Sửa" data-action="edit-owner" data-id="${esc(o.id)}"><i class="fa-solid fa-pen"></i></button>
          <button type="button" class="danger icon-only" title="Xóa" data-action="del-owner" data-id="${esc(o.id)}"><i class="fa-solid fa-trash"></i></button>
        </td>
      `;
      ownerRowsEl.appendChild(tr);
    }

    if (!deletedOwners.length) {
      ownerHistoryRowsEl.innerHTML = `<tr><td colspan="5" class="muted">Chua co owner da xoa.</td></tr>`;
    }
    for (const o of deletedOwners) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${esc(o.id)}</td>
        <td>${esc(o.username)}</td>
        <td>${esc(o.fullName || "")}</td>
        <td>${esc(o.deletedAt || "")}</td>
        <td><button type="button" class="secondary" data-action="restore-owner" data-id="${esc(o.id)}">Khôi phục</button></td>
      `;
      ownerHistoryRowsEl.appendChild(tr);
    }
  };

  const setOwnerTab = (tab) => {
    state.ownerTab = tab === "history" ? "history" : "active";
    const isHistory = state.ownerTab === "history";
    ownerActiveTableEl.hidden = isHistory;
    ownerHistoryTableEl.hidden = !isHistory;
    ownerTabActiveBtn.classList.toggle("active", !isHistory);
    ownerTabHistoryBtn.classList.toggle("active", isHistory);
  };

  const renderPoiAssign = () => {
    poiAssignRowsEl.innerHTML = "";
    const activePois = state.pois.filter((x) => !x.isDeleted);
    if (!activePois.length) {
      poiAssignRowsEl.innerHTML = `<tr><td colspan="5" class="muted">Chua co POI.</td></tr>`;
      poiAssignStatusEl.textContent = "";
      return;
    }
    poiAssignStatusEl.textContent = "";

    for (const p of activePois) {
      const ownerName = p.ownerFullName || p.ownerUsername || "Chua gan";
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${esc(p.id)}</td>
        <td>${esc(p.nameVi || "")}</td>
        <td class="mono">${esc(String(p.latitude))}, ${esc(String(p.longitude))}</td>
        <td>${esc(ownerName)}</td>
        <td><button type="button" class="secondary" data-action="assign-owner" data-id="${esc(p.id)}">Gán</button></td>
      `;
      poiAssignRowsEl.appendChild(tr);
    }
  };

  const ensureOwnerPoiTab = (ownerId) => {
    const key = String(ownerId || "");
    if (!key) return;
    if (!state.ownerPoiTabs.some((x) => x === key)) {
      state.ownerPoiTabs.push(key);
    }
    state.ownerPoiActiveTabId = key;
  };

  const renderOwnerPoiTabs = () => {
    if (!ownerPoiDialogEl || !ownerPoiTabsHeaderEl || !ownerPoiTabsRowsEl) return;
    if (!state.ownerPoiTabs.length) {
      ownerPoiTabsHeaderEl.innerHTML = "";
      ownerPoiTabsRowsEl.innerHTML = "";
      if (ownerPoiDialogEl.open) ownerPoiDialogEl.close();
      return;
    }

    if (!ownerPoiDialogEl.open) {
      ownerPoiDialogEl.showModal();
    }
    ownerPoiTabsHeaderEl.innerHTML = "";
    for (const tabOwnerId of state.ownerPoiTabs) {
      const owner = state.owners.find((o) => String(o.id) === String(tabOwnerId))
        || state.ownerHistory.find((o) => String(o.id) === String(tabOwnerId));
      const label = owner ? owner.username : `Owner #${tabOwnerId}`;
      const btn = document.createElement("button");
      btn.type = "button";
      btn.className = `secondary tab-floating${String(state.ownerPoiActiveTabId) === String(tabOwnerId) ? " active" : ""}`;
      btn.textContent = label;
      btn.addEventListener("click", () => {
        state.ownerPoiActiveTabId = String(tabOwnerId);
        renderOwnerPoiTabs();
      });
      ownerPoiTabsHeaderEl.appendChild(btn);
    }

    const activeOwnerId = String(state.ownerPoiActiveTabId || "");
    const rows = state.pois.filter((p) => !p.isDeleted && String(p.ownerAdminId || "") === activeOwnerId);
    if (!rows.length) {
      ownerPoiTabsRowsEl.innerHTML = `<tr><td colspan="5" class="muted">Owner này chưa có POI được gán.</td></tr>`;
      return;
    }

    ownerPoiTabsRowsEl.innerHTML = "";
    for (const p of rows) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${esc(p.id)}</td>
        <td>${esc(p.nameVi || "")}</td>
        <td class="mono">${esc(String(p.latitude))}, ${esc(String(p.longitude))}</td>
        <td class="mono">${esc(String(p.radiusMeters))}m</td>
        <td>${p.isActive ? '<span class="badge badge-success">Active</span>' : '<span class="badge badge-error">Inactive</span>'}</td>
      `;
      ownerPoiTabsRowsEl.appendChild(tr);
    }
  };

  const reloadData = async () => {
    state.owners = await apiGet("/api/admin/owners");
    state.ownerHistory = await apiGet("/api/admin/owners?includeDeleted=1");
    state.pois = await apiGet("/api/pois/admin");
    renderOwners();
    renderOwnerSelect();
    renderPoiAssign();
  };

  const requireLogin = async () => {
    if (!state.token) {
      authDialogEl.showModal();
      return false;
    }
    try {
      state.me = await apiGet("/api/admin/auth/me");
      updateIdentityUi();
      return true;
    } catch {
      state.token = "";
      localStorage.removeItem("adminToken");
      authDialogEl.showModal();
      return false;
    }
  };

  const wireEvents = () => {
    authFormEl?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const username = ($("#authUsername").value || "").trim();
      const password = ($("#authPassword").value || "").trim();
      authStatusEl.textContent = "Dang dang nhap...";
      try {
        const data = await apiPostJson("/api/admin/auth/login", { username, password });
        state.token = (data?.token || "").trim();
        state.me = data?.user || null;
        localStorage.setItem("adminToken", state.token);
        authStatusEl.textContent = "";
        if (authDialogEl.open) authDialogEl.close();
        updateIdentityUi();
        if (ensureSuperAdmin()) await reloadData();
      } catch (err) {
        const msg = err?.message || String(err);
        authStatusEl.textContent = msg.includes("401")
          ? "Dang nhap that bai. Kiem tra username/password hoac tai khoan da bi xoa mem."
          : msg;
      }
    });

    logoutBtn?.addEventListener("click", async () => {
      try { await apiPostJson("/api/admin/auth/logout", {}); } catch {}
      localStorage.removeItem("adminToken");
      state.token = "";
      state.me = null;
      updateIdentityUi();
      authDialogEl.showModal();
    });

    ownerResetBtn?.addEventListener("click", resetOwnerForm);

    ownerFormEl?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const id = ($("#ownerId").value || "").trim();
      const username = ($("#ownerUsername").value || "").trim();
      const fullName = ($("#ownerFullName").value || "").trim();
      const password = ($("#ownerPassword").value || "").trim();
      ownerStatusEl.textContent = "Dang luu owner...";
      try {
        if (id) {
          await apiPutJson(`/api/admin/owners/${encodeURIComponent(id)}`, { username, fullName, password: password || null });
        } else {
          if (!password) throw new Error("Tao owner moi bat buoc co password.");
          await apiPostJson("/api/admin/owners", { username, fullName, password });
        }
        resetOwnerForm();
        await reloadData();
        ownerStatusEl.textContent = "Luu owner thanh cong.";
      } catch (err) {
        ownerStatusEl.textContent = err?.message || String(err);
      }
    });

    ownerRowsEl?.addEventListener("click", async (e) => {
      const btn = e.target?.closest("button[data-action]");
      if (!btn) return;
      const id = (btn.dataset.id || "").trim();
      if (!id) return;
      const action = btn.dataset.action;
      if (action === "view-owner-pois") {
        ensureOwnerPoiTab(id);
        renderOwnerPoiTabs();
        return;
      }
      if (action === "edit-owner") {
        const o = state.owners.find((x) => String(x.id) === id);
        if (!o) return;
        $("#ownerId").value = o.id;
        $("#ownerUsername").value = o.username || "";
        $("#ownerFullName").value = o.fullName || "";
        $("#ownerPassword").value = "";
        ownerStatusEl.textContent = `Dang sua owner #${id}`;
      } else if (action === "del-owner") {
        if (!confirm(`Xoa owner #${id}?`)) return;
        try {
          await apiDelete(`/api/admin/owners/${encodeURIComponent(id)}`);
          await reloadData();
          ownerStatusEl.textContent = `Da xoa owner #${id}`;
        } catch (err) {
          ownerStatusEl.textContent = err?.message || String(err);
        }
      }
    });

    ownerHistoryRowsEl?.addEventListener("click", async (e) => {
      const btn = e.target?.closest("button[data-action='restore-owner']");
      if (!btn) return;
      const id = (btn.dataset.id || "").trim();
      if (!id) return;
      try {
        await apiPostJson(`/api/admin/owners/${encodeURIComponent(id)}/restore`, {});
        await reloadData();
        ownerStatusEl.textContent = `Da khoi phuc owner #${id}`;
        setOwnerTab("active");
      } catch (err) {
        ownerStatusEl.textContent = err?.message || String(err);
      }
    });

    ownerTabActiveBtn?.addEventListener("click", () => setOwnerTab("active"));
    ownerTabHistoryBtn?.addEventListener("click", () => setOwnerTab("history"));
    ownerPoiTabsCloseBtn?.addEventListener("click", () => {
      state.ownerPoiTabs = [];
      state.ownerPoiActiveTabId = "";
      renderOwnerPoiTabs();
    });

    ownerPoiDialogEl?.addEventListener("close", () => {
      state.ownerPoiTabs = [];
      state.ownerPoiActiveTabId = "";
    });

    poiAssignRowsEl?.addEventListener("click", async (e) => {
      const btn = e.target?.closest("button[data-action='assign-owner']");
      if (!btn) return;
      const poiId = (btn.dataset.id || "").trim();
      if (!poiId) return;
      const ownerId = (assignOwnerIdEl.value || "").trim();
      poiAssignStatusEl.textContent = "Dang cap nhat owner cho POI...";
      try {
        await apiPostJson(`/api/admin/pois/${encodeURIComponent(poiId)}/assign-owner`, { ownerId: ownerId || null });
        await reloadData();
        poiAssignStatusEl.textContent = `Da cap nhat owner cho POI #${poiId}.`;
      } catch (err) {
        poiAssignStatusEl.textContent = err?.message || String(err);
      }
    });
  };

  const init = async () => {
    wireEvents();
    updateIdentityUi();
    const ok = await requireLogin();
    if (!ok) return;
    if (ensureSuperAdmin()) {
      await reloadData();
      setOwnerTab("active");
      renderOwnerPoiTabs();
    }
  };

  init().catch((err) => {
    ownerStatusEl.textContent = err?.message || String(err);
  });
})();

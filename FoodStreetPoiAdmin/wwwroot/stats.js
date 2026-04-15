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

  const audioStatsRowsEl = $("#audioStatsRows");
  const audioStatsSummaryEl = $("#audioStatsSummary");
  const audioStatsStatusEl = $("#audioStatsStatus");
  const audioStatsFromEl = $("#audioStatsFrom");
  const audioStatsToEl = $("#audioStatsTo");
  const audioStatsSortEl = $("#audioStatsSort");
  const audioStatsReloadBtn = $("#audioStatsReloadBtn");

  const state = {
    token: (localStorage.getItem("adminToken") || "").trim(),
    me: null,
    audioStats: [],
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

  const apiPost = async (url) => {
    const res = await fetch(url, { method: "POST", headers: headers() });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json().catch(() => ({}));
  };

  const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

  const setStatus = (msg, isError = false) => {
    if (!audioStatsStatusEl) return;
    audioStatsStatusEl.textContent = msg || "";
    audioStatsStatusEl.classList.toggle("error", !!isError);
  };

  const getTodayIso = () => {
    const now = new Date();
    const year = now.getFullYear();
    const month = String(now.getMonth() + 1).padStart(2, "0");
    const day = String(now.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
  };

  const applyDateLimits = () => {
    if (!audioStatsToEl) return;
    const today = getTodayIso();
    audioStatsToEl.max = today;
    if (audioStatsToEl.value && audioStatsToEl.value > today) {
      audioStatsToEl.value = today;
    }
  };

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
  };

  const formatAudioStatsTime = (value) => {
    if (!value) return "Chua co";
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) return value;
    return parsed.toLocaleString("vi-VN");
  };

  const renderAudioStats = () => {
    audioStatsRowsEl.innerHTML = "";
    const rows = Array.isArray(state.audioStats) ? state.audioStats : [];
    const totalPlays = rows.reduce((sum, item) => sum + Number(item?.playCount || 0), 0);
    audioStatsSummaryEl.textContent = `${rows.length} POI, tong ${totalPlays} lượt bấm phát audio.`;

    if (!rows.length) {
      audioStatsRowsEl.innerHTML = `<tr><td colspan="4" class="muted">Chua co du lieu thong ke trong khoang thoi gian da chon.</td></tr>`;
      return;
    }

    for (const item of rows) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${esc(item.poiId || "")}</td>
        <td>${esc(item.poiName || "") || '<span class="muted">Chua dat ten</span>'}</td>
        <td>${esc(String(item.playCount || 0))}</td>
        <td>${esc(formatAudioStatsTime(item.lastPlayedAt))}</td>
      `;
      audioStatsRowsEl.appendChild(tr);
    }
  };

  const loadAudioStats = async () => {
    applyDateLimits();
    const qs = new URLSearchParams();
    const from = (audioStatsFromEl?.value || "").trim();
    const to = (audioStatsToEl?.value || "").trim();
    const sort = (audioStatsSortEl?.value || "desc").trim() || "desc";
    const today = getTodayIso();
    if (to && to > today) {
      setStatus("Den ngay khong duoc lon hon ngay hien tai.", true);
      return;
    }
    if (from) qs.set("from", from);
    if (to) qs.set("to", to);
    qs.set("sort", sort);

    setStatus("Dang tai thong ke...");
    const data = await apiGet(`/api/admin/reports/audio-plays?${qs.toString()}`);
    state.audioStats = Array.isArray(data?.items) ? data.items : [];
    renderAudioStats();
    setStatus("");
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
      localStorage.removeItem("adminToken");
      state.token = "";
      state.me = null;
      updateIdentityUi();
      authDialogEl.showModal();
      return false;
    }
  };

  const wireEvents = () => {
    authFormEl?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const username = ($("#authUsername")?.value || "").trim();
      const password = ($("#authPassword")?.value || "").trim();
      if (!username || !password) return;

      authStatusEl.textContent = "Dang dang nhap...";
      try {
        const data = await apiPostJson("/api/admin/auth/login", { username, password });
        state.token = (data?.token || "").trim();
        state.me = data?.user || null;
        if (!state.token) throw new Error("Dang nhap that bai.");
        localStorage.setItem("adminToken", state.token);
        authStatusEl.textContent = "";
        if (authDialogEl.open) authDialogEl.close();
        updateIdentityUi();
        await loadAudioStats();
      } catch (err) {
        authStatusEl.textContent = err?.message || String(err);
      }
    });

    logoutBtn?.addEventListener("click", async () => {
      try { await apiPost("/api/admin/auth/logout"); } catch { }
      state.token = "";
      state.me = null;
      localStorage.removeItem("adminToken");
      updateIdentityUi();
      authDialogEl.showModal();
    });

    audioStatsReloadBtn?.addEventListener("click", () => {
      loadAudioStats().catch((err) => setStatus(err?.message || String(err), true));
    });
    audioStatsSortEl?.addEventListener("change", () => {
      loadAudioStats().catch((err) => setStatus(err?.message || String(err), true));
    });
    audioStatsFromEl?.addEventListener("change", () => {
      loadAudioStats().catch((err) => setStatus(err?.message || String(err), true));
    });
    audioStatsToEl?.addEventListener("change", () => {
      loadAudioStats().catch((err) => setStatus(err?.message || String(err), true));
    });
  };

  const init = async () => {
    updateIdentityUi();
    wireEvents();
    applyDateLimits();
    const ok = await requireLogin();
    if (ok) {
      await loadAudioStats();
    }
  };

  init().catch((err) => setStatus(err?.message || String(err), true));
})();

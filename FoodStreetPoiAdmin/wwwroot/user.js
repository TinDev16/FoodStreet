(() => {
  const $ = (s) => document.querySelector(s);

  const authDialogEl = $("#authDialog");
  const authFormEl = $("#authForm");
  const authStatusEl = $("#authStatus");
  const logoutBtn = $("#logoutBtn");
  const sidebarUserNameEl = $("#sidebarUserName");
  const sidebarUserRoleEl = $("#sidebarUserRole");
  const sidebarUserAvatarEl = $("#sidebarUserAvatar");
  const userManageNavEl = $("#userManageNav");

  const userFormEl = $("#userForm");
  const userResetBtn = $("#userResetBtn");
  const userStatusEl = $("#userStatus");
  const userRowsEl = $("#userRows");
  const userHistoryRowsEl = $("#userHistoryRows");
  const userActiveTableEl = $("#userActiveTable");
  const userHistoryTableEl = $("#userHistoryTable");
  const userTabActiveBtn = $("#userTabActiveBtn");
  const userTabHistoryBtn = $("#userTabHistoryBtn");

  const state = {
    token: (localStorage.getItem("adminToken") || "").trim(),
    me: null,
    users: [],
    userHistory: [],
    userTab: "active",
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

  const formatDate = (val) => {
    if (!val) return "";
    try {
      return new Date(val).toLocaleString("vi-VN");
    } catch {
      return val;
    }
  };

  const updateIdentityUi = () => {
    const username = state.me?.username || "Guest";
    const role = state.me?.role || "";
    const fullName = state.me?.fullName || username;
    sidebarUserNameEl.textContent = fullName;
    sidebarUserRoleEl.textContent = role ? role.toUpperCase() : "Chua dang nhap";
    sidebarUserAvatarEl.src = `https://ui-avatars.com/api/?name=${encodeURIComponent(fullName)}&background=4f46e5&color=fff&rounded=true`;
  };

  const ensureSuperAdmin = () => {
    if ((state.me?.role || "").toLowerCase() !== "superadmin") {
      userStatusEl.textContent = "Ban khong co quyen quan ly user.";
      userRowsEl.innerHTML = `<tr><td colspan="6" class="muted">Khong co quyen truy cap.</td></tr>`;
      return false;
    }
    return true;
  };

  const resetUserForm = () => {
    $("#userId").value = "";
    $("#userUsername").value = "";
    $("#userFullName").value = "";
    $("#userPhone").value = "";
    $("#userEmail").value = "";
    $("#userPassword").value = "";
    userStatusEl.textContent = "";
  };

  const renderUsers = () => {
    userRowsEl.innerHTML = "";
    userHistoryRowsEl.innerHTML = "";

    if (!state.users.length) {
      userRowsEl.innerHTML = `<tr><td colspan="6" class="muted text-center pt-20 pb-20">Chưa có user nào đang hoạt động.</td></tr>`;
    }
    for (const u of state.users) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${esc(u.id)}</td>
        <td class="fw-500">${esc(u.username)}</td>
        <td>${esc(u.fullName || "")}</td>
        <td>
          <div class="small">${esc(u.phone)}</div>
          <div class="small muted">${esc(u.email || "")}</div>
        </td>
        <td class="small muted">${esc(formatDate(u.createdAt))}</td>
        <td class="actions-cell">
          <button type="button" class="secondary icon-only" title="Sửa" data-action="edit-user" data-id="${esc(u.id)}"><i class="fa-solid fa-pen pointer-events-none"></i></button>
          <button type="button" class="danger icon-only" title="Xóa" data-action="del-user" data-id="${esc(u.id)}"><i class="fa-solid fa-trash pointer-events-none"></i></button>
        </td>
      `;
      userRowsEl.appendChild(tr);
    }

    if (!state.userHistory.length) {
      userHistoryRowsEl.innerHTML = `<tr><td colspan="5" class="muted text-center pt-20 pb-20">Lịch sử trống.</td></tr>`;
    }
    for (const u of state.userHistory) {
      const tr = document.createElement("tr");
      tr.classList.add("deleted-row");
      tr.innerHTML = `
        <td class="mono">${esc(u.id)}</td>
        <td><i class="muted italic">${esc(u.username)}</i></td>
        <td><i class="muted italic">${esc(u.fullName || "")}</i></td>
        <td class="small muted">${esc(formatDate(u.deletedAt))}</td>
        <td class="actions-cell">
          <button type="button" class="secondary" data-action="restore-user" data-id="${esc(u.id)}"><i class="fa-solid fa-rotate-left"></i> Khôi phục</button>
        </td>
      `;
      userHistoryRowsEl.appendChild(tr);
    }
  };

  const setUserTab = (tab) => {
    state.userTab = tab === "history" ? "history" : "active";
    const isHistory = state.userTab === "history";
    userActiveTableEl.hidden = isHistory;
    userHistoryTableEl.hidden = !isHistory;
    userTabActiveBtn.classList.toggle("active", !isHistory);
    userTabHistoryBtn.classList.toggle("active", isHistory);
  };

  const reloadData = async () => {
    try {
      state.users = await apiGet("/api/admin/users?status=active");
      state.userHistory = await apiGet("/api/admin/users?status=deleted");
      renderUsers();
    } catch (err) {
      userStatusEl.textContent = `Lỗi tải dữ liệu: ${err.message}`;
    }
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
      authStatusEl.textContent = "Đang đăng nhập...";
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
        authStatusEl.textContent = "Sai thông tin đăng nhập hoặc không khả dụng.";
      }
    });

    logoutBtn?.addEventListener("click", () => {
      localStorage.removeItem("adminToken");
      state.token = "";
      state.me = null;
      updateIdentityUi();
      authDialogEl.showModal();
    });

    userResetBtn?.addEventListener("click", resetUserForm);

    userFormEl?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const id = ($("#userId").value || "").trim();
      const username = ($("#userUsername").value || "").trim();
      const fullName = ($("#userFullName").value || "").trim();
      const phone = ($("#userPhone").value || "").trim();
      const email = ($("#userEmail").value || "").trim();
      const password = ($("#userPassword").value || "").trim();

      userStatusEl.textContent = "Đang xử lý...";
      userStatusEl.className = "";

      try {
        if (id) {
          await apiPutJson(`/api/admin/users/${encodeURIComponent(id)}`, { 
            username, fullName, phone, email, password: password || null 
          });
          userStatusEl.textContent = "Cập nhật user thành công.";
        } else {
          if (!password) throw new Error("Tạo user mới bắt buộc có password.");
          await apiPostJson("/api/admin/users", { username, fullName, phone, email, password });
          userStatusEl.textContent = "Thêm user mới thành công.";
        }
        resetUserForm();
        await reloadData();
      } catch (err) {
        userStatusEl.textContent = err?.message || String(err);
        userStatusEl.className = "error";
      }
    });

    const onAction = async (e) => {
      const btn = e.target?.closest("button[data-action]");
      if (!btn) return;
      const id = (btn.dataset.id || "").trim();
      const action = btn.dataset.action;
      if (!id) return;

      try {
        if (action === "edit-user") {
          const u = state.users.find((x) => String(x.id) === id);
          if (!u) return;
          $("#userId").value = u.id;
          $("#userUsername").value = u.username || "";
          $("#userFullName").value = u.fullName || "";
          $("#userPhone").value = u.phone || "";
          $("#userEmail").value = u.email || "";
          $("#userPassword").value = "";
          userStatusEl.textContent = `Đang sửa user #${id}`;
          window.scrollTo({ top: 0, behavior: 'smooth' });
        } else if (action === "del-user") {
          if (!confirm(`Bạn có chắc muốn xóa user #${id}? User này sẽ được chuyển vào mục Thùng rác.`)) return;
          await apiDelete(`/api/admin/users/${encodeURIComponent(id)}`);
          await reloadData();
          userStatusEl.textContent = `Đã xóa user #${id}`;
        } else if (action === "restore-user") {
          await apiPostJson(`/api/admin/users/${encodeURIComponent(id)}/restore`, {});
          await reloadData();
          userStatusEl.textContent = `Đã khôi phục user #${id}`;
          setUserTab("active");
        }
      } catch (err) {
        userStatusEl.textContent = err?.message || String(err);
        userStatusEl.className = "error";
      }
    };

    userRowsEl?.addEventListener("click", onAction);
    userHistoryRowsEl?.addEventListener("click", onAction);

    userTabActiveBtn?.addEventListener("click", () => setUserTab("active"));
    userTabHistoryBtn?.addEventListener("click", () => setUserTab("history"));
  };

  const init = async () => {
    wireEvents();
    updateIdentityUi();
    const ok = await requireLogin();
    if (!ok) return;
    if (ensureSuperAdmin()) {
      await reloadData();
      setUserTab("active");
    }
  };

  init().catch((err) => {
    userStatusEl.textContent = err?.message || String(err);
    userStatusEl.className = "error";
  });
})();

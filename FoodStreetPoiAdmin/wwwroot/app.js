(() => {
  const $ = (sel) => document.querySelector(sel);
  const formEl = $("#poiForm");
  const statusEl = $("#status");
  const rowsEl = $("#poiRows");
  const deletedRowsEl = $("#deletedPoiRows");
  const listHintEl = $("#listHint");
  const createBtn = $("#createBtn");
  const resetBtn = $("#resetBtn");
  const generateQrBtn = $("#generateQrBtn");
  const sourceLangSelect = $("#sourceLang");
  const qrDialogEl = $("#qrDialog");
  const qrLangSelect = $("#qrLang");
  const qrBaseUrlEl = $("#qrBaseUrl");
  const qrPublicUrlEl = $("#qrPublicUrl");
  const qrPreviewImageEl = $("#qrPreviewImage");
  const qrCopyBtn = $("#qrCopyBtn");
  const qrDownloadBtn = $("#qrDownloadBtn");
  const tabActiveBtn = $("#tabActiveBtn");
  const tabHistoryBtn = $("#tabHistoryBtn");
  const activeTable = $("#activeTable");
  const historyTable = $("#historyTable");
  const authDialogEl = $("#authDialog");
  const authFormEl = $("#authForm");
  const authStatusEl = $("#authStatus");
  const logoutBtn = $("#logoutBtn");
  const sidebarUserNameEl = $("#sidebarUserName");
  const sidebarUserRoleEl = $("#sidebarUserRole");
  const sidebarUserAvatarEl = $("#sidebarUserAvatar");
  const ownerManagementCardEl = $("#ownerManagementCard");
  const ownerCreateFormEl = $("#ownerCreateForm");
  const ownerAssignFormEl = $("#ownerAssignForm");
  const assignPoiIdEl = $("#assignPoiId");
  const assignOwnerIdEl = $("#assignOwnerId");
  const ownerManageNavEl = $("#ownerManageNav");

  const state = {
    languages: [],
    selectedSourceLang: "vi",
    qr: {
      poiId: "",
      lang: "vi",
      baseUrl: "",
    },
    current: {
      id: "",
      coreImageUrl: "",
      coreAudioUrl: "",
      translationsByLang: {},
    },
    activeTab: "active",
    auth: {
      token: (localStorage.getItem("adminToken") || "").trim(),
      user: null,
    },
    owners: [],
  };

  const setStatus = (msg, isError = false) => {
    statusEl.textContent = msg || "";
    statusEl.classList.toggle("error", !!isError);
  };

  const safeError = async (res) => {
    try {
      const j = await res.json();
      return j?.error ? `${j.error}${j.detail ? `: ${j.detail}` : ""}` : JSON.stringify(j);
    } catch {
      return `${res.status} ${res.statusText}`;
    }
  };

  const authHeaders = (extra = {}) => {
    const headers = { Accept: "application/json", ...extra };
    if (state.auth.token) {
      headers.Authorization = `Bearer ${state.auth.token}`;
    }
    return headers;
  };

  const apiGet = async (url) => {
    const res = await fetch(url, { headers: authHeaders() });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const apiPostJson = async (url, body) => {
    const res = await fetch(url, {
      method: "POST",
      headers: authHeaders({ "Content-Type": "application/json" }),
      body: JSON.stringify(body),
    });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const apiDelete = async (url) => {
    const res = await fetch(url, { method: "DELETE", headers: authHeaders() });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json().catch(() => ({}));
  };

  const apiPost = async (url) => {
    const res = await fetch(url, { method: "POST", headers: authHeaders() });
    if (!res.ok) throw new Error(await safeError(res));
    return res.json();
  };

  const buildQrImageUrl = (poiId, langCode, download = false) => {
    const qs = new URLSearchParams();
    if (langCode) qs.set("lang", langCode);
    const baseUrl = (state.qr.baseUrl || "").trim();
    if (baseUrl) qs.set("baseUrl", baseUrl);
    qs.set("size", "640");
    if (download) qs.set("download", "1");
    return `/api/pois/${encodeURIComponent(poiId)}/qr.png?${qs.toString()}`;
  };

  const buildPublicLinkUrl = async (poiId, langCode) => {
    const qs = new URLSearchParams();
    if (langCode) qs.set("lang", langCode);
    const baseUrl = (state.qr.baseUrl || "").trim();
    if (baseUrl) qs.set("baseUrl", baseUrl);
    const data = await apiGet(`/api/pois/${encodeURIComponent(poiId)}/public-link?${qs.toString()}`);
    return data?.url || "";
  };

  const fetchQrPngBlobUrl = async (poiId, langCode, download = false) => {
    const url = buildQrImageUrl(poiId, langCode, download);
    const res = await fetch(url, { headers: authHeaders() });
    if (!res.ok) throw new Error(await safeError(res));
    const blob = await res.blob();
    return URL.createObjectURL(blob);
  };

  const ensureTranslationState = (langCode) => {
    const code = String(langCode || "vi").toLowerCase();
    if (!state.current.translationsByLang[code]) {
      state.current.translationsByLang[code] = {
        name: "",
        description: "",
        ttsText: "",
        audioUrl: "",
      };
    }
    return state.current.translationsByLang[code];
  };

  const setCurrentLink = (anchor, emptySpan, url) => {
    if (url) {
      anchor.href = url;
      anchor.textContent = url;
      anchor.hidden = false;
      emptySpan.hidden = true;
    } else {
      anchor.hidden = true;
      emptySpan.hidden = false;
    }
  };

  const parseGps = (raw) => {
    const cleaned = (raw || "").trim().replace(/[()]/g, "");
    if (!cleaned) return null;
    const parts = cleaned.split(/[\s,;]+/).filter(Boolean);
    if (parts.length < 2) return null;

    const lat = Number.parseFloat(parts[0]);
    const lon = Number.parseFloat(parts[1]);
    if (!Number.isFinite(lat) || !Number.isFinite(lon)) return null;
    if (lat < -90 || lat > 90 || lon < -180 || lon > 180) return null;

    return { lat, lon };
  };

  const setGeoFields = (lat, lon) => {
    formEl.elements.namedItem("latitude").value = String(lat);
    formEl.elements.namedItem("longitude").value = String(lon);
    formEl.elements.namedItem("mapLink").value = `https://maps.google.com/?q=${lat},${lon}`;
  };

  const updateFromGpsInput = () => {
    const gpsInput = formEl.elements.namedItem("gps");
    const latInput = formEl.elements.namedItem("latitude");
    const lonInput = formEl.elements.namedItem("longitude");
    const mapLinkInput = formEl.elements.namedItem("mapLink");
    const raw = (gpsInput.value || "").trim();
    if (!raw) {
      latInput.value = "";
      lonInput.value = "";
      mapLinkInput.value = "";
      gpsInput.setCustomValidity("");
      return false;
    }

    const parsed = parseGps(gpsInput.value);
    if (!parsed) {
      gpsInput.setCustomValidity("GPS khong hop le. Vi du: 10.761895379862327, 106.70358792842893");
      return false;
    }

    gpsInput.setCustomValidity("");
    setGeoFields(parsed.lat, parsed.lon);
    return true;
  };

  const saveSourceFieldsToState = () => {
    const tr = ensureTranslationState(state.selectedSourceLang);
    tr.name = (formEl.elements.namedItem("sourceName").value || "").trim();
    tr.description = (formEl.elements.namedItem("sourceDescription").value || "").trim();
    tr.ttsText = (formEl.elements.namedItem("sourceTtsText").value || "").trim();
  };

  const loadSourceFieldsFromState = () => {
    const tr = ensureTranslationState(state.selectedSourceLang);
    formEl.elements.namedItem("sourceName").value = tr.name || "";
    formEl.elements.namedItem("sourceDescription").value = tr.description || "";
    formEl.elements.namedItem("sourceTtsText").value = tr.ttsText || "";
  };

  const setActiveSourceLang = (langCode, options = {}) => {
    if (!options.skipSave) {
      saveSourceFieldsToState();
    }
    state.selectedSourceLang = String(langCode || "vi").toLowerCase();
    sourceLangSelect.value = state.selectedSourceLang;
    loadSourceFieldsFromState();
  };
  const renderList = (items) => {
    rowsEl.innerHTML = "";
    deletedRowsEl.innerHTML = "";

    const activeItems = (items || []).filter((x) => !x.isDeleted);
    const deletedItems = (items || []).filter((x) => !!x.isDeleted);
    listHintEl.textContent = `${state.activeTab === "active" ? activeItems.length : deletedItems.length} POI`;

    for (const item of activeItems) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${escapeHtml(item.id)}</td>
        <td class="fw-500">${escapeHtml(item.nameVi || "")}</td>
        <td class="mono">${escapeHtml(String(item.latitude))}, <br/>${escapeHtml(String(item.longitude))}</td>
        <td class="mono">${escapeHtml(String(item.radiusMeters))}m</td>
        <td class="mono">${escapeHtml(String(item.priority))}</td>
        <td>${item.isActive ? '<span class="badge badge-success">Active</span>' : '<span class="badge badge-error">Inactive</span>'}</td>
        <td class="actions-cell">
          <button type="button" class="secondary icon-only" title="Tạo QR" data-action="qr" data-id="${escapeAttr(item.id)}"><i class="fa-solid fa-qrcode pointer-events-none"></i></button>
          <button type="button" class="secondary icon-only" title="Sửa" data-action="edit" data-id="${escapeAttr(item.id)}"><i class="fa-solid fa-pen pointer-events-none"></i></button>
          <button type="button" class="danger icon-only" title="Xóa" data-action="del" data-id="${escapeAttr(item.id)}"><i class="fa-solid fa-trash pointer-events-none"></i></button>
        </td>
      `;
      rowsEl.appendChild(tr);
    }

    for (const item of deletedItems) {
      const tr = document.createElement("tr");
      const restoreAction = isSuperAdmin()
        ? `<button type="button" class="secondary icon-only" title="Khôi phục" data-action="restore" data-id="${escapeAttr(item.id)}"><i class="fa-solid fa-rotate-left pointer-events-none"></i></button>`
        : "";
      tr.innerHTML = `
        <td class="mono">${escapeHtml(item.id)}</td>
        <td class="fw-500">${escapeHtml(item.nameVi || "")}</td>
        <td>${escapeHtml(item.deletedAt || "")}</td>
        <td class="actions-cell">
          ${restoreAction}
        </td>
      `;
      deletedRowsEl.appendChild(tr);
    }
  };

  const setTab = (tab) => {
    state.activeTab = tab === "history" ? "history" : "active";
    const isHistory = state.activeTab === "history";
    activeTable.hidden = isHistory;
    historyTable.hidden = !isHistory;
    tabActiveBtn.classList.toggle("active", !isHistory);
    tabHistoryBtn.classList.toggle("active", isHistory);
  };

  const escapeHtml = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  const escapeAttr = (s) => escapeHtml(s).replace(/"/g, "&quot;");

  const loadList = async () => {
    const items = await apiGet("/api/pois/admin");
    renderList(items);
    reloadOwnerOptions();
  };

  const resetForm = () => {
    formEl.reset();
    state.current.id = "";
    state.current.coreImageUrl = "";
    state.current.coreAudioUrl = "";
    state.current.translationsByLang = {};

    for (const lang of state.languages) {
      ensureTranslationState(lang.code);
    }

    const defaultLang = state.languages.some((x) => x.code === "vi") ? "vi" : (state.languages[0]?.code || "vi");
    state.selectedSourceLang = defaultLang;
    sourceLangSelect.value = defaultLang;

    $("#currentImageLink").hidden = true;
    $("#currentImageEmpty").hidden = false;
    $("#currentAudioLink").hidden = true;
    $("#currentAudioEmpty").hidden = false;

    formEl.elements.namedItem("gps").value = "";
    formEl.elements.namedItem("latitude").value = "";
    formEl.elements.namedItem("longitude").value = "";
    formEl.elements.namedItem("mapLink").value = "";
    loadSourceFieldsFromState();
    setStatus("");
  };

  const loadPoi = async (id) => {
    const data = await apiGet(`/api/pois/${encodeURIComponent(id)}`);
    resetForm();

    state.current.id = data.id || "";
    state.current.coreImageUrl = data.imageUrl || "";
    state.current.coreAudioUrl = data.audioUrl || "";

    formEl.elements.namedItem("id").value = data.id || "";
    formEl.elements.namedItem("gps").value = `${data.latitude}, ${data.longitude}`;
    updateFromGpsInput();

    formEl.elements.namedItem("radiusMeters").value = String(data.radiusMeters ?? 15);
    formEl.elements.namedItem("priority").value = String(data.priority ?? 0);
    const isActiveInput = formEl.elements.namedItem("isActive");
    if (isActiveInput) {
      isActiveInput.checked = !!data.isActive;
    }

    setCurrentLink($("#currentImageLink"), $("#currentImageEmpty"), state.current.coreImageUrl);
    setCurrentLink($("#currentAudioLink"), $("#currentAudioEmpty"), state.current.coreAudioUrl);

    const translations = Array.isArray(data.translations) ? data.translations : [];
    for (const t of translations) {
      if (!t || !t.langCode) continue;
      const code = String(t.langCode).toLowerCase();
      state.current.translationsByLang[code] = {
        name: t.name || "",
        description: t.description || "",
        ttsText: t.ttsText || "",
        audioUrl: t.audioUrl || "",
      };
    }

    const preferred = state.current.translationsByLang.vi ? "vi" : (translations[0]?.langCode || state.selectedSourceLang || "vi");
    setActiveSourceLang(preferred, { skipSave: true });

    setStatus(`Loaded POI #${id}`);
  };

  const uploadFile = async (file, kind, lang) => {
    const fd = new FormData();
    fd.append("file", file);
    const qs = new URLSearchParams({ kind });
    if (lang) qs.set("lang", lang);

    const res = await fetch(`/api/uploads?${qs.toString()}`, {
      method: "POST",
      body: fd,
      headers: state.auth.token ? { Authorization: `Bearer ${state.auth.token}` } : {},
    });
    if (!res.ok) throw new Error(await safeError(res));
    const j = await res.json();
    return j.url;
  };

  const buildPayloadAndUpload = async () => {
    saveSourceFieldsToState();
    if (!updateFromGpsInput()) {
      throw new Error("GPS khong hop le.");
    }

    const id = formEl.elements.namedItem("id").value.trim();
    const latitude = Number.parseFloat(formEl.elements.namedItem("latitude").value);
    const longitude = Number.parseFloat(formEl.elements.namedItem("longitude").value);
    const radiusMeters = Number.parseFloat(formEl.elements.namedItem("radiusMeters").value);
    const priority = Number.parseInt(formEl.elements.namedItem("priority").value, 10);
    const mapLink = (formEl.elements.namedItem("mapLink").value || "").trim();
    const isActive = formEl.elements.namedItem("isActive")?.checked ?? false;

    let imageUrl = state.current.coreImageUrl;
    let audioUrl = state.current.coreAudioUrl;

    const removeImage = formEl.elements.namedItem("removeImage")?.checked ?? false;
    const removeAudio = formEl.elements.namedItem("removeAudio")?.checked ?? false;
    const imageFile = formEl.elements.namedItem("imageFile")?.files?.[0] || null;
    const audioFile = formEl.elements.namedItem("audioFile")?.files?.[0] || null;

    if (removeImage) imageUrl = "";
    if (removeAudio) audioUrl = "";
    if (imageFile) imageUrl = await uploadFile(imageFile, "image", null);
    if (audioFile) audioUrl = await uploadFile(audioFile, "audio", null);

    const source = ensureTranslationState(state.selectedSourceLang);

    const translations = state.languages.map((lang) => {
      const tr = ensureTranslationState(lang.code);
      return {
        langCode: lang.code,
        name: tr.name || "",
        description: tr.description || "",
        ttsText: tr.ttsText || "",
        audioUrl: tr.audioUrl || "",
      };
    });

    return {
      id: id || null,
      latitude,
      longitude,
      radiusMeters: Number.isFinite(radiusMeters) ? radiusMeters : 15,
      priority: Number.isFinite(priority) ? priority : 0,
      mapLink: mapLink || null,
      imageUrl,
      audioUrl,
      isActive,
      sourceLangCode: state.selectedSourceLang,
      sourceName: source.name || "",
      sourceDescription: source.description || "",
      sourceTtsText: source.ttsText || "",
      translations,
    };
  };

  const buildSourceLangOptions = () => {
    sourceLangSelect.innerHTML = "";
    for (const lang of state.languages) {
      const opt = document.createElement("option");
      opt.value = lang.code;
      opt.textContent = lang.label;
      sourceLangSelect.appendChild(opt);
    }
  };

  const isSuperAdmin = () => (state.auth.user?.role || "").toLowerCase() === "superadmin";

  const updateIdentityUi = () => {
    const username = state.auth.user?.username || "Guest";
    const role = state.auth.user?.role || "";
    const fullName = state.auth.user?.fullName || username;
    sidebarUserNameEl.textContent = fullName;
    sidebarUserRoleEl.textContent = role ? role.toUpperCase() : "Chua dang nhap";
    sidebarUserAvatarEl.src = `https://ui-avatars.com/api/?name=${encodeURIComponent(fullName)}&background=4f46e5&color=fff&rounded=true`;

    const showOwnerMgmt = isSuperAdmin();
    ownerManagementCardEl.hidden = !showOwnerMgmt;
    ownerManageNavEl.hidden = !showOwnerMgmt;
  };

  const requireLogin = async () => {
    if (!state.auth.token) {
      authDialogEl.showModal();
      return false;
    }

    try {
      const me = await apiGet("/api/admin/auth/me");
      state.auth.user = me || null;
      updateIdentityUi();
      return true;
    } catch {
      localStorage.removeItem("adminToken");
      state.auth.token = "";
      state.auth.user = null;
      updateIdentityUi();
      authDialogEl.showModal();
      return false;
    }
  };

  const reloadOwnerOptions = () => {
    if (!assignOwnerIdEl || !assignPoiIdEl) return;
    assignOwnerIdEl.innerHTML = "";
    const unassignOpt = document.createElement("option");
    unassignOpt.value = "";
    unassignOpt.textContent = "— Bỏ gán owner —";
    assignOwnerIdEl.appendChild(unassignOpt);

    for (const owner of state.owners) {
      const opt = document.createElement("option");
      opt.value = owner.id;
      opt.textContent = owner.fullName ? `${owner.username} (${owner.fullName})` : owner.username;
      assignOwnerIdEl.appendChild(opt);
    }

    assignPoiIdEl.innerHTML = "";
    for (const row of rowsEl.querySelectorAll("button[data-action='edit']")) {
      const id = row.dataset.id;
      if (!id) continue;
      const opt = document.createElement("option");
      opt.value = id;
      opt.textContent = `POI #${id}`;
      assignPoiIdEl.appendChild(opt);
    }
  };

  const loadOwners = async () => {
    if (!isSuperAdmin()) {
      state.owners = [];
      reloadOwnerOptions();
      return;
    }

    state.owners = await apiGet("/api/admin/owners");
    reloadOwnerOptions();
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
        state.auth.token = (data?.token || "").trim();
        state.auth.user = data?.user || null;
        if (!state.auth.token) throw new Error("Dang nhap that bai.");
        localStorage.setItem("adminToken", state.auth.token);
        authStatusEl.textContent = "";
        if (authDialogEl.open) authDialogEl.close();
        updateIdentityUi();
        await loadOwners();
        await loadList();
      } catch (err) {
        authStatusEl.textContent = err?.message || String(err);
      }
    });

    logoutBtn?.addEventListener("click", async () => {
      try { await apiPost("/api/admin/auth/logout"); } catch {}
      state.auth.token = "";
      state.auth.user = null;
      localStorage.removeItem("adminToken");
      updateIdentityUi();
      authDialogEl.showModal();
    });

    ownerCreateFormEl?.addEventListener("submit", async (e) => {
      e.preventDefault();
      try {
        await apiPostJson("/api/admin/owners", {
          username: ($("#ownerUsername")?.value || "").trim(),
          fullName: ($("#ownerFullName")?.value || "").trim(),
          password: ($("#ownerPassword")?.value || "").trim(),
        });
        ownerCreateFormEl.reset();
        await loadOwners();
        setStatus("Da tao owner.");
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });

    ownerAssignFormEl?.addEventListener("submit", async (e) => {
      e.preventDefault();
      const poiId = (assignPoiIdEl?.value || "").trim();
      if (!poiId) {
        setStatus("Chon POI can gan owner.", true);
        return;
      }

      try {
        await apiPostJson(`/api/admin/pois/${encodeURIComponent(poiId)}/assign-owner`, {
          ownerId: (assignOwnerIdEl?.value || "").trim() || null,
        });
        setStatus("Da cap nhat owner cho POI.");
        await loadList();
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });

    sourceLangSelect.addEventListener("change", () => setActiveSourceLang(sourceLangSelect.value || "vi"));

    const gpsInput = formEl.elements.namedItem("gps");
    gpsInput.addEventListener("input", () => updateFromGpsInput());
    gpsInput.addEventListener("blur", () => updateFromGpsInput());

    createBtn.addEventListener("click", () => resetForm());
    resetBtn.addEventListener("click", () => resetForm());
    tabActiveBtn?.addEventListener("click", () => {
      setTab("active");
      loadList().catch((err) => setStatus(err?.message || String(err), true));
    });
    tabHistoryBtn?.addEventListener("click", () => {
      setTab("history");
      loadList().catch((err) => setStatus(err?.message || String(err), true));
    });
    generateQrBtn?.addEventListener("click", async () => {
      const currentId = (state.current.id || formEl.elements.namedItem("id").value || "").trim();
      if (!currentId) {
        setStatus("Hay luu hoac chon POI trong danh sach truoc khi tao QR.", true);
        return;
      }

      try {
        await openQrDialog(currentId);
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });

    const onRowAction = async (e) => {
      const btn = e.target?.closest("button[data-action]");
      if (!btn) return;
      const action = btn.dataset.action;
      const id = btn.dataset.id;
      if (!id) return;

      try {
        if (action === "qr") {
          await openQrDialog(id);
        } else if (action === "edit") {
          await loadPoi(id);
        } else if (action === "del") {
          if (!confirm(`Xoa POI #${id}?`)) return;
          setStatus("Soft deleting...");
          await apiDelete(`/api/pois/${encodeURIComponent(id)}`);
          await loadList();
          resetForm();
          setStatus(`Soft deleted POI #${id}`);
        } else if (action === "restore") {
          if (!confirm(`Restore POI #${id}?`)) return;
          setStatus("Restoring...");
          await apiPost(`/api/pois/${encodeURIComponent(id)}/restore`);
          await loadList();
          setStatus(`Restored POI #${id}`);
        }
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    };
    rowsEl.addEventListener("click", onRowAction);
    deletedRowsEl.addEventListener("click", onRowAction);

    qrLangSelect.addEventListener("change", async () => {
      if (!state.qr.poiId) return;
      state.qr.lang = qrLangSelect.value || "vi";
      await refreshQrDialog();
    });

    qrBaseUrlEl?.addEventListener("change", async () => {
      state.qr.baseUrl = (qrBaseUrlEl.value || "").trim();
      localStorage.setItem("poiPublicBaseUrl", state.qr.baseUrl);
      if (!state.qr.poiId) return;
      await refreshQrDialog();
    });

    qrDialogEl?.addEventListener("close", () => {
      try {
        if (state.qr.previewBlobUrl) URL.revokeObjectURL(state.qr.previewBlobUrl);
        if (state.qr.downloadBlobUrl) URL.revokeObjectURL(state.qr.downloadBlobUrl);
      } catch {}
      state.qr.previewBlobUrl = "";
      state.qr.downloadBlobUrl = "";
    });

    qrCopyBtn.addEventListener("click", async () => {
      const text = (qrPublicUrlEl.value || "").trim();
      if (!text) return;

      try {
        await navigator.clipboard.writeText(text);
        setStatus("Da copy public URL.");
      } catch {
        qrPublicUrlEl.focus();
        qrPublicUrlEl.select();
        setStatus("Khong the copy tu dong. Ban co the copy thu cong.");
      }
    });

    formEl.addEventListener("submit", async (e) => {
      e.preventDefault();
      updateFromGpsInput();
      if (!formEl.reportValidity()) return;

      setStatus("Saving...");
      try {
        const payload = await buildPayloadAndUpload();
        const res = await apiPostJson("/api/pois", payload);
        await loadList();
        await loadPoi(res.id);
        setStatus(`Saved POI #${res.id}`);
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });
  };

  const init = async () => {
    setStatus("Loading...");
    updateIdentityUi();
    state.languages = await apiGet("/api/languages");
    buildSourceLangOptions();

    for (const lang of state.languages) {
      ensureTranslationState(lang.code);
    }

    const defaultLang = state.languages.some((x) => x.code === "vi") ? "vi" : (state.languages[0]?.code || "vi");
    state.selectedSourceLang = defaultLang;
    sourceLangSelect.value = defaultLang;

    wireEvents();
    setTab("active");
    resetForm();
    const ok = await requireLogin();
    if (ok) {
      await loadOwners();
      await loadList();
    }
    setStatus("");
  };

  /** Khi chạy trên Render/host công khai (không phải localhost), dùng origin làm fallback nếu API lỗi. */
  const publicOriginFallback = () => {
    if (typeof window === "undefined" || !window.location?.hostname) return "";
    const h = window.location.hostname.toLowerCase();
    if (h === "localhost" || h === "127.0.0.1" || h === "::1") return "";
    return `${window.location.protocol}//${window.location.host}`;
  };

  const refreshQrDialog = async () => {
    const poiId = state.qr.poiId;
    const langCode = state.qr.lang || "vi";
    const publicUrl = await buildPublicLinkUrl(poiId, langCode);

    qrPublicUrlEl.value = publicUrl;
    if (state.qr.previewBlobUrl) {
      URL.revokeObjectURL(state.qr.previewBlobUrl);
    }
    if (state.qr.downloadBlobUrl) {
      URL.revokeObjectURL(state.qr.downloadBlobUrl);
    }

    state.qr.previewBlobUrl = await fetchQrPngBlobUrl(poiId, langCode, false);
    state.qr.downloadBlobUrl = await fetchQrPngBlobUrl(poiId, langCode, true);

    qrPreviewImageEl.src = state.qr.previewBlobUrl;
    qrDownloadBtn.href = state.qr.downloadBlobUrl;
    qrDownloadBtn.setAttribute("download", `poi-${poiId}.png`);
  };

  const openQrDialog = async (poiId) => {
    if (!qrDialogEl || !poiId) return;

    if (!state.languages.length) {
      state.languages = await apiGet("/api/languages");
    }

    if (!qrLangSelect.options.length) {
      for (const lang of state.languages) {
        const opt = document.createElement("option");
        opt.value = lang.code;
        opt.textContent = lang.label;
        qrLangSelect.appendChild(opt);
      }
    }

    state.qr.poiId = String(poiId);
    state.qr.lang = state.selectedSourceLang || "vi";
    try {
      const baseInfo = await apiGet("/api/public/base-url");
      state.qr.baseUrl = (baseInfo?.baseUrl || "").trim() || publicOriginFallback();
      if (state.qr.baseUrl) {
        localStorage.setItem("poiPublicBaseUrl", state.qr.baseUrl);
      }
    } catch {
      const fromStorage = (localStorage.getItem("poiPublicBaseUrl") || "").trim();
      state.qr.baseUrl = fromStorage || publicOriginFallback();
    }

    qrLangSelect.value = state.qr.lang;
    if (qrBaseUrlEl) {
      qrBaseUrlEl.value = state.qr.baseUrl || "";
    }
    qrPreviewImageEl.removeAttribute("src");
    qrPublicUrlEl.value = "";
    if (state.qr.previewBlobUrl) {
      URL.revokeObjectURL(state.qr.previewBlobUrl);
      state.qr.previewBlobUrl = "";
    }
    if (state.qr.downloadBlobUrl) {
      URL.revokeObjectURL(state.qr.downloadBlobUrl);
      state.qr.downloadBlobUrl = "";
    }

    qrDialogEl.showModal();
    setStatus("Dang tao QR...");
    try {
      await refreshQrDialog();
      setStatus("");
    } catch (err) {
      setStatus(err?.message || String(err), true);
    }
  };

  init().catch((err) => setStatus(err?.message || String(err), true));
})();

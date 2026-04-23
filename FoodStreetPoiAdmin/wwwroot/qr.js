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

  const qrFormEl = $("#qrForm");
  const qrStatusEl = $("#qrStatus");
  const qrRowsEl = $("#qrRows");
  const qrPoiIdEl = $("#qrPoiId");
  const qrPriceEl = $("#qrPrice");
  const qrLangEl = $("#qrLang");
  const qrViewBtn = $("#qrViewBtn");
  const qrResetBtn = $("#qrResetBtn");

  const qrDialogEl = $("#qrDialog");
  const qrPublicUrlEl = $("#qrPublicUrl");
  const qrPreviewImageEl = $("#qrPreviewImage");
  const qrCopyBtn = $("#qrCopyBtn");
  const qrDownloadBtn = $("#qrDownloadBtn");

  const state = {
    token: (localStorage.getItem("adminToken") || "").trim(),
    me: null,
    pois: [],
    languages: [],
    qr: {
      previewBlobUrl: "",
      downloadBlobUrl: "",
      baseUrl: "",
    },
  };

  const setStatus = (msg, isError = false) => {
    if (!qrStatusEl) return;
    qrStatusEl.textContent = msg || "";
    qrStatusEl.classList.toggle("error", !!isError);
  };

  const safeError = async (res) => {
    try {
      const data = await res.json();
      return data?.error ? `${data.error}${data.detail ? `: ${data.detail}` : ""}` : JSON.stringify(data);
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

  const formatCurrency = (value) => {
    const amount = Number(value);
    if (!Number.isFinite(amount) || amount <= 0) return "Miễn phí";
    return `${Math.round(amount).toLocaleString("vi-VN")} đ`;
  };

  const formatDateTime = (value) => {
    if (!value) return "";
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) return String(value);
    return parsed.toLocaleString("vi-VN");
  };

  const normalizePoi = (raw) => ({
    id: String(raw?.id || ""),
    name: raw?.nameVi || raw?.name || "",
    price: Number(raw?.price || 0),
    updatedAt: raw?.updatedAt || "",
    isDeleted: !!raw?.isDeleted,
  });

  const updateIdentityUi = () => {
    const username = state.me?.username || "Guest";
    const role = state.me?.role || "";
    const roleCode = role.toLowerCase();
    const fullName = state.me?.fullName || username;
    sidebarUserNameEl.textContent = fullName;
    sidebarUserRoleEl.textContent = role ? role.toUpperCase() : "Chua dang nhap";
    sidebarUserAvatarEl.src = `https://ui-avatars.com/api/?name=${encodeURIComponent(fullName)}&background=4f46e5&color=fff&rounded=true`;
    if (ownerManageNavEl) {
      ownerManageNavEl.hidden = roleCode === "owner";
    }
    if (userManageNavEl) {
      userManageNavEl.hidden = roleCode !== "superadmin";
    }
    if (qrManageNavEl) {
      qrManageNavEl.hidden = !roleCode;
    }
    if (monitoringNavEl) {
      monitoringNavEl.hidden = !(roleCode === "superadmin" || roleCode === "owner");
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
      state.me = null;
      localStorage.removeItem("adminToken");
      updateIdentityUi();
      authDialogEl.showModal();
      return false;
    }
  };

  const loadLanguages = async () => {
    try {
      const data = await apiGet("/api/languages");
      state.languages = Array.isArray(data) ? data : [];
    } catch {
      state.languages = [];
    }
    if (!state.languages.length) {
      state.languages = [{ code: "vi", label: "Vietnamese" }];
    }
    qrLangEl.innerHTML = "";
    for (const lang of state.languages) {
      const opt = document.createElement("option");
      opt.value = lang.code;
      opt.textContent = lang.label || lang.code;
      qrLangEl.appendChild(opt);
    }
    qrLangEl.value = state.languages.some((x) => x.code === "vi") ? "vi" : state.languages[0].code;
  };

  const loadPois = async (keepPoiId = "") => {
    const data = await apiGet("/api/pois/admin");
    const rows = Array.isArray(data) ? data : [];
    state.pois = rows.map(normalizePoi).filter((x) => x.id && !x.isDeleted);
    state.pois.sort((a, b) => a.name.localeCompare(b.name, "vi"));
    renderPoiSelect();
    renderTable();

    if (keepPoiId && state.pois.some((x) => x.id === keepPoiId)) {
      qrPoiIdEl.value = keepPoiId;
    }
    syncFormWithSelectedPoi();
  };

  const renderPoiSelect = () => {
    qrPoiIdEl.innerHTML = "";
    for (const poi of state.pois) {
      const opt = document.createElement("option");
      opt.value = poi.id;
      opt.textContent = `${poi.id} - ${poi.name || "Chưa đặt tên"}`;
      qrPoiIdEl.appendChild(opt);
    }
  };

  const renderTable = () => {
    qrRowsEl.innerHTML = "";
    if (!state.pois.length) {
      qrRowsEl.innerHTML = `<tr><td colspan="5" class="muted">Chưa có POI để quản lý QR.</td></tr>`;
      return;
    }

    for (const poi of state.pois) {
      const tr = document.createElement("tr");
      tr.innerHTML = `
        <td class="mono">${esc(poi.id)}</td>
        <td class="fw-500">${esc(poi.name || "Chưa đặt tên")}</td>
        <td>${esc(formatCurrency(poi.price))}</td>
        <td class="small muted">${esc(formatDateTime(poi.updatedAt) || "—")}</td>
        <td class="actions-cell">
          <button type="button" class="secondary icon-only" title="Sửa" data-action="edit" data-id="${esc(poi.id)}"><i class="fa-solid fa-pen pointer-events-none"></i></button>
          <button type="button" class="secondary icon-only" title="Xem QR" data-action="view" data-id="${esc(poi.id)}"><i class="fa-regular fa-eye pointer-events-none"></i></button>
          <button type="button" class="secondary icon-only" title="Tạo QR" data-action="create" data-id="${esc(poi.id)}"><i class="fa-solid fa-qrcode pointer-events-none"></i></button>
        </td>
      `;
      qrRowsEl.appendChild(tr);
    }
  };

  const syncFormWithSelectedPoi = () => {
    const poi = state.pois.find((x) => x.id === qrPoiIdEl.value) || state.pois[0];
    if (!poi) return;
    qrPoiIdEl.value = poi.id;
    qrPriceEl.value = String(Math.max(0, Math.round(Number(poi.price) || 0)));
  };

  const publicOriginFallback = () => {
    if (typeof window === "undefined" || !window.location?.hostname) return "";
    const h = window.location.hostname.toLowerCase();
    if (h === "localhost" || h === "127.0.0.1" || h === "::1") return "";
    return `${window.location.protocol}//${window.location.host}`;
  };

  const loadBaseUrl = async () => {
    try {
      const baseInfo = await apiGet("/api/public/base-url");
      const value = (baseInfo?.baseUrl || "").trim() || publicOriginFallback();
      if (value) {
        state.qr.baseUrl = value;
        localStorage.setItem("poiPublicBaseUrl", value);
      }
      return;
    } catch {
      const fromStorage = (localStorage.getItem("poiPublicBaseUrl") || "").trim();
      state.qr.baseUrl = fromStorage || publicOriginFallback();
    }
  };

  const fetchQrPngBlobUrl = async (poiId, langCode, baseUrl, download = false) => {
    const qs = new URLSearchParams();
    if (langCode) qs.set("lang", langCode);
    if (baseUrl) qs.set("baseUrl", baseUrl);
    qs.set("size", "640");
    if (download) qs.set("download", "1");
    const url = `/api/pois/${encodeURIComponent(poiId)}/qr.png?${qs.toString()}`;
    const res = await fetch(url, { headers: headers() });
    if (!res.ok) throw new Error(await safeError(res));
    const blob = await res.blob();
    return URL.createObjectURL(blob);
  };

  const openQrPreview = async (poiId, langCode) => {
    const baseUrl = (state.qr.baseUrl || "").trim();
    if (!poiId) throw new Error("Chưa chọn POI.");
    const qs = new URLSearchParams();
    if (langCode) qs.set("lang", langCode);
    if (baseUrl) qs.set("baseUrl", baseUrl);
    const link = await apiGet(`/api/pois/${encodeURIComponent(poiId)}/public-link?${qs.toString()}`);
    qrPublicUrlEl.value = link?.url || "";

    if (state.qr.previewBlobUrl) URL.revokeObjectURL(state.qr.previewBlobUrl);
    if (state.qr.downloadBlobUrl) URL.revokeObjectURL(state.qr.downloadBlobUrl);
    state.qr.previewBlobUrl = await fetchQrPngBlobUrl(poiId, langCode, baseUrl, false);
    state.qr.downloadBlobUrl = await fetchQrPngBlobUrl(poiId, langCode, baseUrl, true);
    qrPreviewImageEl.src = state.qr.previewBlobUrl;
    qrDownloadBtn.href = state.qr.downloadBlobUrl;
    qrDownloadBtn.setAttribute("download", `poi-${poiId}.png`);
    if (!qrDialogEl.open) qrDialogEl.showModal();
  };

  const buildPriceUpdatePayload = (detail, newPrice) => {
    const translations = Array.isArray(detail?.translations)
      ? detail.translations.map((t) => ({
          langCode: String(t?.langCode || "").toLowerCase(),
          name: t?.name || "",
          description: t?.description || "",
          ttsText: t?.ttsText || "",
          audioUrl: t?.audioUrl || "",
        })).filter((t) => t.langCode)
      : [];

    return {
      id: String(detail?.id || ""),
      latitude: Number(detail?.latitude ?? 0),
      longitude: Number(detail?.longitude ?? 0),
      radiusMeters: Number(detail?.radiusMeters ?? 15),
      priority: Number(detail?.priority ?? 0),
      price: Math.max(0, Number(newPrice) || 0),
      mapLink: detail?.mapLink || null,
      imageUrl: detail?.imageUrl || "",
      audioUrl: detail?.audioUrl || "",
      isActive: !!detail?.isActive,
      sourceLangCode: "",
      sourceName: "",
      sourceDescription: "",
      sourceTtsText: "",
      translations,
    };
  };

  const updatePoiPrice = async (poiId, newPrice) => {
    const detail = await apiGet(`/api/pois/${encodeURIComponent(poiId)}`);
    const payload = buildPriceUpdatePayload(detail, newPrice);
    await apiPostJson("/api/pois", payload);
  };

  const handleGenerate = async (savePriceFirst) => {
    const poiId = (qrPoiIdEl.value || "").trim();
    const langCode = (qrLangEl.value || "vi").trim();
    const newPrice = Number(qrPriceEl.value || 0);
    if (!poiId) {
      setStatus("Chưa chọn POI.", true);
      return;
    }
    if (!Number.isFinite(newPrice) || newPrice < 0) {
      setStatus("Giá mở khóa không hợp lệ.", true);
      return;
    }

    const current = state.pois.find((x) => x.id === poiId);
    if (savePriceFirst && current && Math.round(current.price) !== Math.round(newPrice)) {
      setStatus("Đang cập nhật giá mở khóa...");
      await updatePoiPrice(poiId, newPrice);
      await loadPois(poiId);
    }

    setStatus("Đang tạo QR...");
    await openQrPreview(poiId, langCode);
    setStatus("Đã tạo QR.");
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
        await loadLanguages();
        await loadBaseUrl();
        await loadPois();
      } catch (err) {
        authStatusEl.textContent = err?.message || String(err);
      }
    });

    logoutBtn?.addEventListener("click", async () => {
      try { await apiPost("/api/admin/auth/logout"); } catch {}
      state.token = "";
      state.me = null;
      localStorage.removeItem("adminToken");
      updateIdentityUi();
      authDialogEl.showModal();
    });

    qrPoiIdEl?.addEventListener("change", syncFormWithSelectedPoi);

    qrFormEl?.addEventListener("submit", async (e) => {
      e.preventDefault();
      try {
        await handleGenerate(true);
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });

    qrViewBtn?.addEventListener("click", async () => {
      try {
        await handleGenerate(false);
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });

    qrResetBtn?.addEventListener("click", () => {
      syncFormWithSelectedPoi();
      setStatus("");
    });

    qrRowsEl?.addEventListener("click", async (e) => {
      const btn = e.target?.closest("button[data-action]");
      if (!btn) return;
      const poiId = (btn.dataset.id || "").trim();
      const action = btn.dataset.action;
      if (!poiId) return;

      qrPoiIdEl.value = poiId;
      syncFormWithSelectedPoi();
      if (action === "edit") {
        setStatus(`Đang sửa QR của POI #${poiId}`);
        window.scrollTo({ top: 0, behavior: "smooth" });
        return;
      }

      try {
        await handleGenerate(action === "create");
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });

    qrCopyBtn?.addEventListener("click", async () => {
      const text = (qrPublicUrlEl.value || "").trim();
      if (!text) return;
      try {
        await navigator.clipboard.writeText(text);
        setStatus("Đã copy public URL.");
      } catch {
        qrPublicUrlEl.focus();
        qrPublicUrlEl.select();
        setStatus("Không thể copy tự động. Bạn có thể copy thủ công.");
      }
    });

    qrDialogEl?.addEventListener("close", () => {
      if (state.qr.previewBlobUrl) URL.revokeObjectURL(state.qr.previewBlobUrl);
      if (state.qr.downloadBlobUrl) URL.revokeObjectURL(state.qr.downloadBlobUrl);
      state.qr.previewBlobUrl = "";
      state.qr.downloadBlobUrl = "";
    });

    $("#masterQrViewBtn")?.addEventListener("click", async () => {
      try {
        await openMasterQrPreview();
      } catch (err) {
        setStatus(err?.message || String(err), true);
      }
    });
  };

  const fetchMasterQrPngBlobUrl = async (download = false) => {
    const qs = new URLSearchParams();
    if (download) qs.set("download", "1");
    const baseUrl = (state.qr.baseUrl || "").trim();
    if (baseUrl) qs.set("baseUrl", baseUrl);
    const url = `/api/admin/qr/master.png?${qs.toString()}`;
    const res = await fetch(url, { headers: headers() });
    if (!res.ok) throw new Error(await safeError(res));
    const blob = await res.blob();
    return URL.createObjectURL(blob);
  };

  const openMasterQrPreview = async () => {
    setStatus("Đang tải QR Tổng...");
    const baseUrl = (state.qr.baseUrl || "").trim();
    qrPublicUrlEl.value = `${baseUrl.trimEnd('/')}/list.html`;

    if (state.qr.previewBlobUrl) URL.revokeObjectURL(state.qr.previewBlobUrl);
    if (state.qr.downloadBlobUrl) URL.revokeObjectURL(state.qr.downloadBlobUrl);
    state.qr.previewBlobUrl = await fetchMasterQrPngBlobUrl(false);
    state.qr.downloadBlobUrl = await fetchMasterQrPngBlobUrl(true);
    qrPreviewImageEl.src = state.qr.previewBlobUrl;
    qrDownloadBtn.href = state.qr.downloadBlobUrl;
    qrDownloadBtn.setAttribute("download", `foodstreet-master-qr.png`);
    if (!qrDialogEl.open) qrDialogEl.showModal();
    setStatus("");
  };

  const init = async () => {
    wireEvents();
    updateIdentityUi();
    const ok = await requireLogin();
    if (!ok) return;
    await loadLanguages();
    await loadBaseUrl();
    await loadPois();
    setStatus("");
  };

  init().catch((err) => setStatus(err?.message || String(err), true));
})();

(() => {
  const $ = (s) => document.querySelector(s);
  const poiListEl = $("#poiList");
  const searchInput = $("#searchInput");
  const loadingOverlay = $("#loadingOverlay");
  const masterLockOverlay = $("#masterLockOverlay");
  const masterLockPrice = $("#masterLockPrice");
  const masterUnlockBtn = $("#masterUnlockBtn");
  const appInstallPromptEl = $("#appInstallPrompt");
  const openAppBtnEl = $("#openAppBtn");
  const dismissInstallPromptBtnEl = $("#dismissInstallPromptBtn");

  const INSTALL_PROMPT_DISABLED_KEY = "poiDisableAppInstallPrompt";
  const PUBLIC_GUEST_USER_ID_KEY = "poiPublicGuestUserId";

  const state = {
    allPois: [],
    filteredPois: [],
    lang: 'vi',
    guestUserId: 0,
    sessionId: "",
    appHandOffDone: false
  };

  const ensureGuestUserId = () => {
    const fromStorage = Number.parseInt(localStorage.getItem(PUBLIC_GUEST_USER_ID_KEY) || "", 10);
    if (Number.isFinite(fromStorage) && fromStorage > 0) {
      state.guestUserId = fromStorage;
      return fromStorage;
    }
    const created = Math.floor(Date.now() / 1000);
    localStorage.setItem(PUBLIC_GUEST_USER_ID_KEY, String(created));
    state.guestUserId = created;
    return created;
  };

  const toLangCode = (raw) => {
    const cleaned = String(raw || "").trim().toLowerCase();
    if (!cleaned) return "vi";
    const normalized = cleaned.replace("_", "-").split("-")[0];
    return normalized || "vi";
  };

  const buildAppDeepLinkUrl = (langCode) => {
    const params = new URLSearchParams();
    if (langCode) params.set("lang", toLangCode(langCode));
    return `foodstreet://open-list?${params.toString()}`;
  };

  const shouldDisableInstallPrompt = () => localStorage.getItem(INSTALL_PROMPT_DISABLED_KEY) === "1";
  const hideInstallPrompt = () => { if (appInstallPromptEl) appInstallPromptEl.hidden = true; };
  const showInstallPrompt = () => { if (appInstallPromptEl) appInstallPromptEl.hidden = false; };

  const tryOpenAppDeepLink = async (deepLinkUrl) => {
    if (!deepLinkUrl) return false;
    return new Promise((resolve) => {
      let completed = false;
      const finish = (opened) => {
        if (completed) return;
        completed = true;
        document.removeEventListener("visibilitychange", onVisibilityChange);
        window.removeEventListener("pagehide", onPageHide);
        resolve(Boolean(opened));
      };
      const onVisibilityChange = () => { if (document.visibilityState === "hidden") finish(true); };
      const onPageHide = () => finish(true);
      document.addEventListener("visibilitychange", onVisibilityChange, { once: true });
      window.addEventListener("pagehide", onPageHide, { once: true });
      window.location.href = deepLinkUrl;
      window.setTimeout(() => finish(document.visibilityState === "hidden"), 1600);
    });
  };

  const initAppHandOff = async () => {
    if (state.appHandOffDone) return;
    state.appHandOffDone = true;
    hideInstallPrompt();
    if (shouldDisableInstallPrompt()) return;
    const opened = await tryOpenAppDeepLink(buildAppDeepLinkUrl(state.lang));
    if (!opened) showInstallPrompt();
  };

  const formatCurrency = (value) => {
    const amount = Number(value);
    if (!Number.isFinite(amount) || amount <= 0) return "Miễn phí";
    return `${Math.round(amount).toLocaleString("vi-VN")} đ`;
  };

  const renderList = () => {
    poiListEl.innerHTML = "";
    if (state.filteredPois.length === 0) {
      poiListEl.innerHTML = `<div class="no-results">
        <i class="fa-solid fa-store-slash" style="font-size: 3rem; margin-bottom: 15px; display: block;"></i>
        Không tìm thấy địa điểm nào phù hợp.
      </div>`;
      return;
    }

    state.filteredPois.forEach(poi => {
      const card = document.createElement("a");
      card.className = "poi-item";
      card.href = `/poi.html?id=${encodeURIComponent(poi.id)}`;
      
      const priceText = formatCurrency(poi.price);
      const imgSrc = poi.imageUrl || "https://images.unsplash.com/photo-1555396273-367ea4eb4db5?w=500&auto=format&fit=crop&q=60";

      card.innerHTML = `
        <div class="poi-img-wrap">
          <img src="${imgSrc}" alt="${poi.name || 'POI'}">
          ${poi.price > 0 ? `<div class="poi-price-tag">${priceText}</div>` : ''}
        </div>
        <div class="poi-info">
          <h3 class="poi-name">${poi.name || 'Chưa đặt tên'}</h3>
          <p class="poi-desc">${poi.description || 'Khám phá ngay điểm đến hấp dẫn này.'}</p>
          <div class="poi-footer">
            <span style="font-size: 0.8rem; color: #94a3b8;">#${poi.id}</span>
            <span class="view-btn">Chi tiết <i class="fa-solid fa-chevron-right" style="font-size: 0.7rem;"></i></span>
          </div>
        </div>
      `;
      poiListEl.appendChild(card);
    });
  };

  const fetchMasterQrInfo = async () => {
    try {
      const sid = localStorage.getItem('session_id') || "";
      const res = await fetch(`/api/public/master/info?sessionId=${encodeURIComponent(sid)}&userId=${encodeURIComponent(state.guestUserId)}`);
      if (res.ok) {
        const data = await res.json();
        if (data.unlockFee > 0 && !data.isUnlocked) {
          masterLockPrice.textContent = formatCurrency(data.unlockFee);
          masterLockOverlay.hidden = false;
          poiListEl.classList.add("blur-content");
        } else {
          masterLockOverlay.hidden = true;
          poiListEl.classList.remove("blur-content");
        }
      }
    } catch (e) {}
  };

  const unlockMasterQr = async () => {
    try {
      masterUnlockBtn.disabled = true;
      const originalText = masterUnlockBtn.textContent;
      masterUnlockBtn.textContent = "Đang mở khóa...";
      const sid = localStorage.getItem('session_id') || "";
      const res = await fetch(`/api/public/master/unlock`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ sessionId: sid, userId: String(state.guestUserId) })
      });
      if (res.ok) {
        const data = await res.json();
        if (data.isUnlocked) {
          masterLockOverlay.hidden = true;
          poiListEl.classList.remove("blur-content");
        }
      } else {
        alert("Đã có lỗi xảy ra");
      }
    } catch (e) {
      alert("Lỗi kết nối");
    } finally {
      masterUnlockBtn.textContent = "Mở khóa ngay";
      masterUnlockBtn.disabled = false;
    }
  };

  const fetchData = async () => {
    try {
      const res = await fetch(`/api/pois?lang=${state.lang}`);
      if (!res.ok) throw new Error("Không thể tải dữ liệu.");
      const data = await res.json();
      state.allPois = Array.isArray(data) ? data : [];
      state.filteredPois = state.allPois;
      renderList();
      await fetchMasterQrInfo();
    } catch (err) {
      console.error(err);
      poiListEl.innerHTML = `<div class="no-results">Đã có lỗi xảy ra khi tải danh sách.</div>`;
    } finally {
      loadingOverlay.style.opacity = "0";
      setTimeout(() => loadingOverlay.style.display = "none", 300);
    }
  };

  const handleSearch = () => {
    const query = searchInput.value.toLowerCase().trim();
    if (!query) {
      state.filteredPois = state.allPois;
    } else {
      state.filteredPois = state.allPois.filter(poi => 
        (poi.name || "").toLowerCase().includes(query) || 
        (poi.description || "").toLowerCase().includes(query) ||
        String(poi.id).includes(query)
      );
    }
    renderList();
  };

  const getOrCreateDeviceId = () => {
    let id = localStorage.getItem("device_id");
    if (!id) {
        try { id = crypto.randomUUID(); } catch(e) { id = 'dev_' + Date.now() + '_' + Math.random().toString(36).substring(2); }
        localStorage.setItem("device_id", id);
    }
    return id;
  };

  const trackActivity = async (action) => {
    try {
        const sid = localStorage.getItem('session_id') || ('web_' + Date.now() + '_' + Math.random().toString(36).substring(2));
        localStorage.setItem('session_id', sid);
        const did = getOrCreateDeviceId();
        const payload = {
            action, platform: 'web', sessionId: sid, deviceId: did, language: state.lang,
            poiId: null, deviceType: 'web'
        };
        await fetch('/api/public/pois/track-activity', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
    } catch(e) {}
  };

  const init = () => {
    const params = new URLSearchParams(window.location.search);
    state.lang = params.get("lang") || "vi";

    ensureGuestUserId();
    searchInput.addEventListener("input", handleSearch);
    masterUnlockBtn?.addEventListener("click", unlockMasterQr);

    openAppBtnEl?.addEventListener("click", async () => await tryOpenAppDeepLink(buildAppDeepLinkUrl(state.lang)));
    dismissInstallPromptBtnEl?.addEventListener("click", () => { localStorage.setItem(INSTALL_PROMPT_DISABLED_KEY, "1"); hideInstallPrompt(); });

    fetchData();

    // Track initial view and start ping loop
    trackActivity('view_list'); 
    setInterval(() => trackActivity('ping'), 15000);
    
    initAppHandOff();
  };

  init();
})();

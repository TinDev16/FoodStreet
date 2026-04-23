(() => {
  const $ = (s) => document.querySelector(s);
  const poiListEl = $("#poiList");
  const searchInput = $("#searchInput");
  const loadingOverlay = $("#loadingOverlay");

  const state = {
    allPois: [],
    filteredPois: [],
    lang: 'vi'
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

  const fetchData = async () => {
    try {
      const res = await fetch(`/api/pois?lang=${state.lang}`);
      if (!res.ok) throw new Error("Không thể tải dữ liệu.");
      const data = await res.json();
      state.allPois = Array.isArray(data) ? data : [];
      state.filteredPois = state.allPois;
      renderList();
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

  const init = () => {
    const params = new URLSearchParams(window.location.search);
    state.lang = params.get("lang") || "vi";

    searchInput.addEventListener("input", handleSearch);
    fetchData();
  };

  init();
})();

const token = localStorage.getItem('adminToken');
if (!token) {
  window.location.replace('/');
}

document.getElementById('logoutBtn')?.addEventListener('click', () => {
  localStorage.removeItem('adminToken');
  window.location.replace('/');
});

const themeToggleBtn = document.getElementById('themeToggleBtn');
if (themeToggleBtn) {
  const currentTheme = localStorage.getItem('poi_theme') || 'light';
  document.documentElement.setAttribute('data-theme', currentTheme);
  
  themeToggleBtn.addEventListener('click', () => {
    const newTheme = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', newTheme);
    localStorage.setItem('poi_theme', newTheme);
    if (window.activityChartVar) {
      window.activityChartVar.updateOptions({
        theme: { mode: newTheme }
      });
    }
  });
}

function parseJwt(token) {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(atob(base64).split('').map(function(c) {
      return '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2);
    }).join(''));
    return JSON.parse(jsonPayload);
  } catch (e) {
    return null;
  }
}

const payload = parseJwt(token);
if (payload) {
  document.getElementById('sidebarUserName').textContent = payload.admin_full_name || payload.admin_username || 'Admin';
  const role = (payload.admin_role || "").toLowerCase();
  const isSuper = role === 'superadmin';
  const isOwner = role === 'owner';
  
  if (isSuper) {
    document.getElementById('ownerManageNav').hidden = false;
    document.getElementById('qrManageNav').hidden = false;
  }
  
  const monitoringNav = document.getElementById('monitoringNav');
  if (monitoringNav) {
    monitoringNav.hidden = !(isSuper || isOwner);
  }

  if (isOwner) {
    document.getElementById('sidebarUserRole').textContent = 'Chủ sở hữu';
  }
}

const filterPlatform = document.getElementById('filterPlatform');
const filterPeriod = document.getElementById('filterPeriod');
const filterFrom = document.getElementById('filterFrom');
const filterTo = document.getElementById('filterTo');
const filterLang = document.getElementById('filterLang');
const filterPoiSort = document.getElementById('filterPoiSort');
const btnRefresh = document.getElementById('btnRefresh');
const customDateRange = document.getElementById('customDateRange');

filterPeriod?.addEventListener('change', () => {
  if (filterPeriod.value === 'custom') {
    customDateRange.style.display = 'flex';
  } else {
    customDateRange.style.display = 'none';
  }
});

btnRefresh?.addEventListener('click', () => loadDashboard());

// When a user clicks one of the 4 stat cards, the Hourly + Ranking charts below
// will be scoped to that single action. Click again to clear.
const ACTION_META = {
  online: { label: 'User online',     countHeader: 'Số user online', icon: 'fa-signal' },
  audio:  { label: 'Lượt nghe Audio', countHeader: 'Lượt nghe',      icon: 'fa-headphones' },
  qr:     { label: 'Lượt quét QR',    countHeader: 'Lượt quét',      icon: 'fa-qrcode' },
  view:   { label: 'Lượt xem POI',    countHeader: 'Lượt xem',       icon: 'fa-eye' },
};

let selectedAction = null;

function applyCardActiveState() {
  document.querySelectorAll('.stat-card.clickable').forEach(card => {
    card.classList.toggle('active', card.dataset.action === selectedAction);
  });
}

function updateChartLabels() {
  const hourlySub = document.getElementById('hourlyChartSubtitle');
  const rankSub = document.getElementById('poiRankingSubtitle');
  const rankHeader = document.getElementById('poiRankingCountHeader');
  const meta = selectedAction ? ACTION_META[selectedAction] : null;

  if (hourlySub) {
    if (selectedAction === 'online') {
      hourlySub.textContent = 'Số user duy nhất online trong mỗi khung giờ';
    } else if (meta) {
      hourlySub.textContent = `Chỉ tính: ${meta.label}`;
    } else {
      hourlySub.textContent = 'Tất cả tương tác (trừ ping)';
    }
  }
  if (rankSub) {
    if (selectedAction === 'online') {
      rankSub.textContent = 'Không áp dụng cho Online theo thời gian thực';
    } else if (meta) {
      rankSub.textContent = `Chỉ tính: ${meta.label}`;
    } else {
      rankSub.textContent = 'Điểm = Quét QR × 3 + Nghe Audio × 2 + Xem POI × 1';
    }
  }
  if (rankHeader) {
    rankHeader.textContent = meta ? meta.countHeader : 'Tổng tương tác';
  }
}

document.querySelectorAll('.stat-card.clickable').forEach(card => {
  const handler = () => {
    const next = card.dataset.action;
    selectedAction = (selectedAction === next) ? null : next;
    applyCardActiveState();
    loadDashboard();
  };
  card.addEventListener('click', handler);
  card.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      handler();
    }
  });
});

async function loadDashboard() {
  try {
    const params = new URLSearchParams();
    if (filterPlatform.value !== 'all') params.set('platform', filterPlatform.value);
    if (filterPeriod.value) params.set('period', filterPeriod.value);
    if (filterPeriod.value === 'custom') {
      params.set('from', filterFrom.value);
      params.set('to', filterTo.value);
    }
    if (filterLang.value !== 'all') params.set('lang', filterLang.value);
    if (filterPoiSort.value) params.set('poiSort', filterPoiSort.value);
    if (selectedAction) params.set('action', selectedAction);

    const res = await fetch('/api/admin/reports/user-activities?' + params.toString(), {
      headers: { 'Authorization': 'Bearer ' + token }
    });
    
    if (res.status === 401 || res.status === 403) {
      localStorage.removeItem('adminToken');
      window.location.replace('/');
      return;
    }

    if (!res.ok) throw new Error('Failed to load dashboard data');
    const data = await res.json();
    
    document.getElementById('valOnlineNow').textContent = data.onlineNow || 0;
    document.getElementById('valAudioPlays').textContent = data.periodAudioPlays || 0;
    document.getElementById('valQrScans').textContent = data.periodQrScans || 0;
    document.getElementById('valViews').textContent = data.periodViews || 0;
    
    updateChartLabels();
    renderMainChart(data.chartData || []);
    renderHourlyChart(data.hourlyData || [], selectedAction);
    renderPoiRanking(data.topPois || [], selectedAction);
  } catch(e) {
    console.error(e);
  }
}

function renderMainChart(chartData) {
  const dates = [...new Set(chartData.map(d => d.date))].sort();
  
  const audioPlays = dates.map(dt => {
    return chartData.filter(d => d.date === dt && d.action === 'play_audio')
      .reduce((s, d) => s + d.count, 0);
  });
  
  const qrScans = dates.map(dt => {
    return chartData.filter(d => d.date === dt && d.action === 'scan_qr')
      .reduce((s, d) => s + d.count, 0);
  });
  
  const views = dates.map(dt => {
    return chartData.filter(d => d.date === dt && d.action === 'view_poi')
      .reduce((s, d) => s + d.count, 0);
  });

  const isDark = document.documentElement.getAttribute('data-theme') === 'dark';

  const options = {
    series: [
      { name: 'Lượt xem POI', data: views },
      { name: 'Nghe Audio', data: audioPlays },
      { name: 'Quét QR', data: qrScans }
    ],
    colors: ['#0ea5e9', '#f59e0b', '#10b981'],
    chart: {
      type: 'area',
      height: 350,
      fontFamily: 'Outfit, sans-serif',
      toolbar: { show: false },
      background: 'transparent'
    },
    theme: { mode: isDark ? 'dark' : 'light' },
    dataLabels: { enabled: false },
    stroke: { curve: 'smooth', width: 3 },
    xaxis: {
      categories: dates.map(d => d.split('-').slice(1).reverse().join('/')),
      labels: { rotate: -45 }
    },
    fill: {
      type: 'gradient',
      gradient: { shadeIntensity: 1, opacityFrom: 0.4, opacityTo: 0.05, stops: [0, 100] }
    },
    legend: { position: 'top', horizontalAlign: 'right' },
    tooltip: { theme: isDark ? 'dark' : 'light' }
  };

  const chartElement = document.querySelector("#activityChart");
  if (chartElement) {
    if (window.activityChartVar) {
      window.activityChartVar.updateOptions(options);
    } else {
      window.activityChartVar = new ApexCharts(chartElement, options);
      window.activityChartVar.render();
    }
  }
}

function renderHourlyChart(hourlyData, action) {
  const hours = Array.from({ length: 24 }, (_, i) => i.toString().padStart(2, '0'));
  const counts = hours.map(h => {
    const found = hourlyData.find(d => parseInt(d.hour) == parseInt(h));
    return found ? found.count : 0;
  });

  const meta = action ? ACTION_META[action] : null;
  const seriesName = meta ? meta.countHeader : 'Tổng tương tác';

  const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
  const options = {
    series: [{ name: seriesName, data: counts }],
    chart: {
      type: 'bar',
      height: 350,
      fontFamily: 'Outfit, sans-serif',
      toolbar: { show: false },
      background: 'transparent'
    },
    colors: ['#4f46e5'],
    theme: { mode: isDark ? 'dark' : 'light' },
    plotOptions: {
      bar: { borderRadius: 4, columnWidth: '60%' }
    },
    dataLabels: { enabled: false },
    xaxis: {
      categories: hours.map(h => h + 'h'),
    },
    tooltip: { theme: isDark ? 'dark' : 'light' }
  };

  const chartElement = document.querySelector("#hourlyChart");
  if (chartElement) {
    if (window.hourlyChartVar) {
      window.hourlyChartVar.updateOptions(options);
    } else {
      window.hourlyChartVar = new ApexCharts(chartElement, options);
      window.hourlyChartVar.render();
    }
  }
}

function renderPoiRanking(topPois, action) {
  const container = document.getElementById('poiRankingRows');
  if (!container) return;

  if (action === 'online') {
    container.innerHTML = '<tr><td colspan="4" class="text-center muted" style="padding: 40px;">Xếp hạng không áp dụng cho bộ lọc "Online theo thời gian thực". Bỏ chọn thẻ này để xem xếp hạng.</td></tr>';
    return;
  }
  
  if (topPois.length === 0) {
    const emptyMsg = action
      ? `Không có ${ACTION_META[action]?.label?.toLowerCase() || 'dữ liệu'} trong khoảng thời gian này.`
      : 'Không có dữ liệu trong khoảng thời gian này.';
    container.innerHTML = `<tr><td colspan="4" class="text-center muted">${emptyMsg}</td></tr>`;
    return;
  }

  const maxCount = Math.max(...topPois.map(p => p.count));

  container.innerHTML = topPois.map((poi, idx) => {
    const percentage = (poi.count / maxCount) * 100;
    const rankClass = idx === 0 ? 'rank-1' : (idx === 1 ? 'rank-2' : (idx === 2 ? 'rank-3' : 'rank-other'));

    return `
      <tr>
        <td><div class="rank-badge ${rankClass}">${idx + 1}</div></td>
        <td>
          <div style="font-weight: 600; color: var(--text-color);">${poi.name}</div>
          <div style="font-size: 0.75rem; color: var(--text-muted);">ID: ${poi.poiId}</div>
        </td>
        <td class="text-right"><strong style="color: var(--text-color);">${poi.count.toLocaleString()}</strong></td>
        <td>
          <div class="progress-bar-wrap">
            <div class="progress-bar-fill" style="width: ${percentage}%;"></div>
          </div>
        </td>
      </tr>
    `;
  }).join('');
}

// Initial load
loadDashboard();

// Refresh online count every 30s (only refresh online value to save resources)
setInterval(async () => {
    try {
        const res = await fetch('/api/admin/reports/user-activities?fields=onlineNow', {
            headers: { 'Authorization': 'Bearer ' + token }
        });
        if (res.ok) {
            const data = await res.json();
            const el = document.getElementById('valOnlineNow');
            if (el) el.textContent = data.onlineNow || 0;
        }
    } catch(e) {}
}, 30000);

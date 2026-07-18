const state = { routes: [], currentDetailId: null };

// ---- Elements ----
const loginScreen = document.getElementById('login-screen');
const appEl = document.getElementById('app');
const loginForm = document.getElementById('login-form');
const loginError = document.getElementById('login-error');
const whoEl = document.getElementById('who');

// ---- Init ----
(async function init() {
  const me = await fetch('/api/me').then(r => r.json());
  if (me.loggedIn) {
    showApp(me.name);
  } else {
    loginScreen.classList.remove('hidden');
  }
})();

// ---- Auth ----
loginForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  loginError.textContent = '';
  const name = document.getElementById('login-name').value;
  const passcode = document.getElementById('login-passcode').value;
  const res = await fetch('/api/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, passcode })
  });
  const data = await res.json();
  if (!res.ok) {
    loginError.textContent = data.error || 'Login failed';
    return;
  }
  showApp(data.name);
});

document.getElementById('logout-btn').addEventListener('click', async () => {
  await fetch('/api/logout', { method: 'POST' });
  location.reload();
});

function showApp(name) {
  loginScreen.classList.add('hidden');
  appEl.classList.remove('hidden');
  whoEl.textContent = `Logged in as ${name}`;
  loadRoutes();
}

// ---- Search / filters ----
const filterIds = ['f-q', 'f-crag', 'f-type', 'f-tag'];
filterIds.forEach(id => {
  document.getElementById(id).addEventListener('input', debounce(loadRoutes, 250));
});

function debounce(fn, ms) {
  let t;
  return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); };
}

async function loadRoutes() {
  const params = new URLSearchParams();
  const q = document.getElementById('f-q').value.trim();
  const crag = document.getElementById('f-crag').value.trim();
  const type = document.getElementById('f-type').value;
  const tag = document.getElementById('f-tag').value.trim();
  if (q) params.set('q', q);
  if (crag) params.set('crag', crag);
  if (type) params.set('type', type);
  if (tag) params.set('tag', tag);

  const res = await fetch('/api/routes?' + params.toString());
  if (res.status === 401) { location.reload(); return; }
  const routes = await res.json();
  state.routes = routes;
  renderRoutes();
}

function renderRoutes() {
  const list = document.getElementById('routes-list');
  const empty = document.getElementById('empty-state');
  const count = document.getElementById('results-count');
  count.textContent = `Routes (${state.routes.length})`;

  list.innerHTML = '';
  if (state.routes.length === 0) {
    empty.classList.remove('hidden');
    return;
  }
  empty.classList.add('hidden');

  for (const r of state.routes) {
    const card = document.createElement('div');
    card.className = 'route-card';
    card.innerHTML = `
      <span class="grade-pill">${escapeHtml(r.grade)}</span>
      <div class="rc-main">
        <div class="rc-name">${escapeHtml(r.name)}</div>
        <div class="rc-sub">${escapeHtml(r.crag)}</div>
      </div>
      <span class="rc-type">${escapeHtml(r.type)}</span>
      <span class="rc-ticks">${r.tickCount} log${r.tickCount === 1 ? '' : 's'}</span>
    `;
    card.addEventListener('click', () => openDetail(r.id));
    list.appendChild(card);
  }
}

function escapeHtml(str) {
  const d = document.createElement('div');
  d.textContent = str ?? '';
  return d.innerHTML;
}

// ---- Add / Edit route modal ----
const routeModal = document.getElementById('route-modal');
const routeForm = document.getElementById('route-form');

document.getElementById('add-route-btn').addEventListener('click', () => openRouteModal());
document.getElementById('modal-cancel').addEventListener('click', () => routeModal.classList.add('hidden'));

function openRouteModal(route) {
  document.getElementById('modal-title').textContent = route ? 'Edit route' : 'Add a route';
  document.getElementById('route-id').value = route ? route.id : '';
  document.getElementById('r-name').value = route ? route.name : '';
  document.getElementById('r-crag').value = route ? route.crag : '';
  document.getElementById('r-grade').value = route ? route.grade : '';
  document.getElementById('r-grade-system').value = route ? route.grade_system : 'YDS';
  document.getElementById('r-type').value = route ? route.type : 'sport';
  document.getElementById('r-description').value = route ? route.description : '';
  document.getElementById('r-tags').value = route ? route.tags : '';
  document.getElementById('r-fa').value = route ? route.first_ascent : '';
  routeModal.classList.remove('hidden');
}

routeForm.addEventListener('submit', async (e) => {
  e.preventDefault();
  const id = document.getElementById('route-id').value;
  const payload = {
    name: document.getElementById('r-name').value.trim(),
    crag: document.getElementById('r-crag').value.trim(),
    grade: document.getElementById('r-grade').value.trim(),
    grade_system: document.getElementById('r-grade-system').value,
    type: document.getElementById('r-type').value,
    description: document.getElementById('r-description').value.trim(),
    tags: document.getElementById('r-tags').value.trim(),
    first_ascent: document.getElementById('r-fa').value.trim(),
  };
  const url = id ? `/api/routes/${id}` : '/api/routes';
  const method = id ? 'PUT' : 'POST';
  await fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  });
  routeModal.classList.add('hidden');
  if (id && state.currentDetailId == id) openDetail(id);
  loadRoutes();
});

// ---- Detail / ticks modal ----
const detailModal = document.getElementById('detail-modal');

async function openDetail(id) {
  state.currentDetailId = id;
  const route = await fetch(`/api/routes/${id}`).then(r => r.json());
  document.getElementById('d-name').textContent = route.name;
  document.getElementById('d-meta').textContent = `${route.grade} (${route.grade_system}) \u00b7 ${route.crag} \u00b7 ${route.type}`;
  document.getElementById('d-description').textContent = route.description || 'No description yet.';
  document.getElementById('d-fa').textContent = route.first_ascent ? `First ascent: ${route.first_ascent}` : '';

  const tagsEl = document.getElementById('d-tags');
  tagsEl.innerHTML = '';
  (route.tags || '').split(',').map(t => t.trim()).filter(Boolean).forEach(t => {
    const chip = document.createElement('span');
    chip.className = 'tag-chip';
    chip.textContent = t;
    tagsEl.appendChild(chip);
  });

  document.getElementById('t-route-id').value = id;
  renderTicks(route.ticks);

  document.getElementById('d-edit').onclick = () => { detailModal.classList.add('hidden'); openRouteModal(route); };
  document.getElementById('d-delete').onclick = () => deleteRoute(id);

  detailModal.classList.remove('hidden');
}

function renderTicks(ticks) {
  const el = document.getElementById('ticks-list');
  el.innerHTML = '';
  if (!ticks.length) {
    el.innerHTML = '<p class="hint" style="font-size:13px;">No ascents logged yet.</p>';
    return;
  }
  for (const t of ticks) {
    const row = document.createElement('div');
    row.className = 'tick-row';
    row.innerHTML = `
      <div>
        <span class="tr-who">${escapeHtml(t.climber)}</span>
        &middot; ${escapeHtml(t.date)}
        ${t.rating ? `&middot; ${'\u2605'.repeat(t.rating)}` : ''}
        ${t.notes ? `<div class="tr-notes">${escapeHtml(t.notes)}</div>` : ''}
      </div>
      <button class="tick-remove" data-id="${t.id}" title="Remove">&times;</button>
    `;
    row.querySelector('.tick-remove').addEventListener('click', async () => {
      await fetch(`/api/ticks/${t.id}`, { method: 'DELETE' });
      openDetail(state.currentDetailId);
    });
    el.appendChild(row);
  }
}

document.getElementById('tick-form').addEventListener('submit', async (e) => {
  e.preventDefault();
  const routeId = document.getElementById('t-route-id').value;
  const payload = {
    date: document.getElementById('t-date').value,
    rating: document.getElementById('t-rating').value || null,
    notes: document.getElementById('t-notes').value.trim(),
  };
  await fetch(`/api/routes/${routeId}/ticks`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload)
  });
  document.getElementById('tick-form').reset();
  openDetail(routeId);
  loadRoutes();
});

async function deleteRoute(id) {
  if (!confirm('Delete this route and all its logged ascents?')) return;
  await fetch(`/api/routes/${id}`, { method: 'DELETE' });
  detailModal.classList.add('hidden');
  loadRoutes();
}

// Close modals on overlay click
[routeModal, detailModal].forEach(modal => {
  modal.addEventListener('click', (e) => {
    if (e.target === modal) modal.classList.add('hidden');
  });
});

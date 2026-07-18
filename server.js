require('dotenv').config();
const express = require('express');
const session = require('express-session');
const path = require('path');
const db = require('./db');

const app = express();
const PORT = process.env.PORT || 3000;
const PASSCODE = process.env.PASSCODE || 'climb123';

app.use(express.json());
app.use(session({
  secret: process.env.SESSION_SECRET || 'change-me-please',
  resave: false,
  saveUninitialized: false,
  cookie: { maxAge: 1000 * 60 * 60 * 24 * 30 } // 30 days
}));

// --- Auth ---
function requireAuth(req, res, next) {
  if (req.session && req.session.loggedIn) return next();
  return res.status(401).json({ error: 'Not logged in' });
}

app.post('/api/login', (req, res) => {
  const { passcode, name } = req.body;
  if (passcode !== PASSCODE) {
    return res.status(401).json({ error: 'Wrong passcode' });
  }
  req.session.loggedIn = true;
  req.session.name = (name || 'Anonymous').trim().slice(0, 40);
  res.json({ ok: true, name: req.session.name });
});

app.post('/api/logout', (req, res) => {
  req.session.destroy(() => res.json({ ok: true }));
});

app.get('/api/me', (req, res) => {
  if (req.session && req.session.loggedIn) {
    return res.json({ loggedIn: true, name: req.session.name });
  }
  res.json({ loggedIn: false });
});

// --- Routes CRUD + search ---
app.get('/api/routes', requireAuth, (req, res) => {
  const { q, crag, type, tag, minGrade, maxGrade } = req.query;
  let sql = 'SELECT * FROM routes WHERE 1=1';
  const params = [];

  if (q) {
    sql += ' AND (name LIKE ? OR description LIKE ?)';
    params.push(`%${q}%`, `%${q}%`);
  }
  if (crag) {
    sql += ' AND crag LIKE ?';
    params.push(`%${crag}%`);
  }
  if (type) {
    sql += ' AND type = ?';
    params.push(type);
  }
  if (tag) {
    sql += ' AND tags LIKE ?';
    params.push(`%${tag}%`);
  }
  sql += ' ORDER BY created_at DESC';

  let routes = db.prepare(sql).all(...params);

  // Attach tick count + avg rating
  const tickStmt = db.prepare('SELECT COUNT(*) as count, AVG(rating) as avgRating FROM ticks WHERE route_id = ?');
  routes = routes.map(r => {
    const stats = tickStmt.get(r.id);
    return { ...r, tickCount: stats.count, avgRating: stats.avgRating };
  });

  res.json(routes);
});

app.get('/api/routes/:id', requireAuth, (req, res) => {
  const route = db.prepare('SELECT * FROM routes WHERE id = ?').get(req.params.id);
  if (!route) return res.status(404).json({ error: 'Not found' });
  const ticks = db.prepare('SELECT * FROM ticks WHERE route_id = ? ORDER BY date DESC').all(req.params.id);
  res.json({ ...route, ticks });
});

app.post('/api/routes', requireAuth, (req, res) => {
  const { name, crag, grade, grade_system, type, description, tags, first_ascent } = req.body;
  if (!name || !crag || !grade) {
    return res.status(400).json({ error: 'name, crag, and grade are required' });
  }
  const stmt = db.prepare(`
    INSERT INTO routes (name, crag, grade, grade_system, type, description, tags, first_ascent, added_by)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);
  const info = stmt.run(
    name, crag, grade, grade_system || 'YDS', type || 'sport',
    description || '', tags || '', first_ascent || '', req.session.name
  );
  res.json({ id: info.lastInsertRowid });
});

app.put('/api/routes/:id', requireAuth, (req, res) => {
  const { name, crag, grade, grade_system, type, description, tags, first_ascent } = req.body;
  const stmt = db.prepare(`
    UPDATE routes SET name=?, crag=?, grade=?, grade_system=?, type=?, description=?, tags=?, first_ascent=?
    WHERE id=?
  `);
  stmt.run(name, crag, grade, grade_system, type, description, tags, first_ascent, req.params.id);
  res.json({ ok: true });
});

app.delete('/api/routes/:id', requireAuth, (req, res) => {
  db.prepare('DELETE FROM routes WHERE id = ?').run(req.params.id);
  res.json({ ok: true });
});

// --- Ticks (log an ascent) ---
app.post('/api/routes/:id/ticks', requireAuth, (req, res) => {
  const { date, rating, notes } = req.body;
  if (!date) return res.status(400).json({ error: 'date is required' });
  const stmt = db.prepare(`
    INSERT INTO ticks (route_id, climber, date, rating, notes) VALUES (?, ?, ?, ?, ?)
  `);
  const info = stmt.run(req.params.id, req.session.name, date, rating || null, notes || '');
  res.json({ id: info.lastInsertRowid });
});

app.delete('/api/ticks/:id', requireAuth, (req, res) => {
  db.prepare('DELETE FROM ticks WHERE id = ?').run(req.params.id);
  res.json({ ok: true });
});

// --- Static frontend ---
app.use(express.static(path.join(__dirname, 'public')));

app.listen(PORT, () => {
  console.log(`Climbing log running at http://localhost:${PORT}`);
});

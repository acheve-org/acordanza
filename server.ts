import 'dotenv/config';
import express, { type NextFunction, type Request, type Response } from 'express';
import session from 'express-session';
import path from 'path';
import db from './db.ts';

declare module 'express-session' {
  interface SessionData {
    loggedIn: boolean;
    name: string;
  }
}

interface Route {
  id: number;
  name: string;
  crag: string;
  grade: string;
  grade_system: string;
  type: string;
  description: string;
  tags: string;
  first_ascent: string;
  added_by: string;
  created_at: string;
}

interface Tick {
  id: number;
  route_id: number;
  climber: string;
  date: string;
  rating: number | null;
  notes: string;
  created_at: string;
}

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
function requireAuth(req: Request, res: Response, next: NextFunction) {
  if (req.session && req.session.loggedIn) return next();
  return res.status(401).json({ error: 'Not logged in' });
}

app.post('/api/login', (req: Request, res: Response) => {
  const { passcode, name } = req.body;
  if (passcode !== PASSCODE) {
    return res.status(401).json({ error: 'Wrong passcode' });
  }
  req.session.loggedIn = true;
  req.session.name = (name || 'Anonymous').trim().slice(0, 40);
  res.json({ ok: true, name: req.session.name });
});

app.post('/api/logout', (req: Request, res: Response) => {
  req.session.destroy(() => res.json({ ok: true }));
});

app.get('/api/me', (req: Request, res: Response) => {
  if (req.session && req.session.loggedIn) {
    return res.json({ loggedIn: true, name: req.session.name });
  }
  res.json({ loggedIn: false });
});

// --- Routes CRUD + search ---
app.get('/api/routes', requireAuth, (req: Request, res: Response) => {
  const { q, crag, type, tag } = req.query;
  let sql = 'SELECT * FROM routes WHERE 1=1';
  const params: string[] = [];

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
    params.push(type as string);
  }
  if (tag) {
    sql += ' AND tags LIKE ?';
    params.push(`%${tag}%`);
  }
  sql += ' ORDER BY created_at DESC';

  const routes = db.prepare(sql).all(...params) as Route[];

  // Attach tick count + avg rating
  const tickStmt = db.prepare('SELECT COUNT(*) as count, AVG(rating) as avgRating FROM ticks WHERE route_id = ?');
  const routesWithStats = routes.map(r => {
    const stats = tickStmt.get(r.id) as { count: number; avgRating: number | null };
    return { ...r, tickCount: stats.count, avgRating: stats.avgRating };
  });

  res.json(routesWithStats);
});

app.get('/api/routes/:id', requireAuth, (req: Request, res: Response) => {
  const route = db.prepare('SELECT * FROM routes WHERE id = ?').get(req.params.id as string) as Route | undefined;
  if (!route) return res.status(404).json({ error: 'Not found' });
  const ticks = db.prepare('SELECT * FROM ticks WHERE route_id = ? ORDER BY date DESC').all(req.params.id as string) as Tick[];
  res.json({ ...route, ticks });
});

app.post('/api/routes', requireAuth, (req: Request, res: Response) => {
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

app.put('/api/routes/:id', requireAuth, (req: Request, res: Response) => {
  const { name, crag, grade, grade_system, type, description, tags, first_ascent } = req.body;
  const stmt = db.prepare(`
    UPDATE routes SET name=?, crag=?, grade=?, grade_system=?, type=?, description=?, tags=?, first_ascent=?
    WHERE id=?
  `);
  stmt.run(name, crag, grade, grade_system, type, description, tags, first_ascent, req.params.id as string);
  res.json({ ok: true });
});

app.delete('/api/routes/:id', requireAuth, (req: Request, res: Response) => {
  db.prepare('DELETE FROM routes WHERE id = ?').run(req.params.id as string);
  res.json({ ok: true });
});

// --- Ticks (log an ascent) ---
app.post('/api/routes/:id/ticks', requireAuth, (req: Request, res: Response) => {
  const { date, rating, notes } = req.body;
  if (!date) return res.status(400).json({ error: 'date is required' });
  const stmt = db.prepare(`
    INSERT INTO ticks (route_id, climber, date, rating, notes) VALUES (?, ?, ?, ?, ?)
  `);
  const info = stmt.run(req.params.id as string, req.session.name, date, rating || null, notes || '');
  res.json({ id: info.lastInsertRowid });
});

app.delete('/api/ticks/:id', requireAuth, (req: Request, res: Response) => {
  db.prepare('DELETE FROM ticks WHERE id = ?').run(req.params.id as string);
  res.json({ ok: true });
});

// --- Static frontend ---
app.use(express.static(path.join(import.meta.dir, 'public')));

app.listen(PORT, () => {
  console.log(`Acordanza running at http://localhost:${PORT}`);
});

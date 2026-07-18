const { Database } = require('bun:sqlite');
const path = require('path');

const db = new Database(path.join(__dirname, 'climbing.db'));
db.exec('PRAGMA journal_mode = WAL');

db.exec(`
CREATE TABLE IF NOT EXISTS routes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  crag TEXT NOT NULL,
  grade TEXT NOT NULL,
  grade_system TEXT NOT NULL DEFAULT 'YDS',
  type TEXT NOT NULL DEFAULT 'sport',
  description TEXT DEFAULT '',
  tags TEXT DEFAULT '',
  first_ascent TEXT DEFAULT '',
  added_by TEXT DEFAULT '',
  created_at TEXT DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS ticks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  route_id INTEGER NOT NULL REFERENCES routes(id) ON DELETE CASCADE,
  climber TEXT NOT NULL,
  date TEXT NOT NULL,
  rating INTEGER,
  notes TEXT DEFAULT '',
  created_at TEXT DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_routes_grade ON routes(grade);
CREATE INDEX IF NOT EXISTS idx_routes_crag ON routes(crag);
CREATE INDEX IF NOT EXISTS idx_ticks_route ON ticks(route_id);
`);

module.exports = db;

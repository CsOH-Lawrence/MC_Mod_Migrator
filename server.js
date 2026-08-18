/* MC Mod Migrator — deliberately dependency-free local server. */
const http = require('http');
const fs = require('fs/promises');
const fssync = require('fs');
const path = require('path');
const { execFile } = require('child_process');
const { promisify } = require('util');
const { inflateRawSync } = require('zlib');

const execFileAsync = promisify(execFile);
const PORT = 3728;
const jobs = new Map();
let nativeFolderPicker = null;
let logDirectory = path.join(__dirname, 'logs');

function json(res, status, data) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
  res.end(JSON.stringify(data));
}
async function body(req) {
  const parts = [];
  for await (const chunk of req) parts.push(chunk);
  return JSON.parse(Buffer.concat(parts).toString('utf8') || '{}');
}
function cleanString(value) { return String(value || '').replace(/[\r\n]/g, ' ').trim(); }
function normalize(value) { return cleanString(value).toLowerCase().replace(/[^a-z0-9]/g, ''); }
function isExactProject(project, mod) {
  const keys = [mod.id, mod.name].map(normalize).filter(Boolean);
  const values = [project.slug, project.title, project.name].map(normalize).filter(Boolean);
  return keys.some(key => values.includes(key));
}
function isCore(mod) {
  const id = String(mod.id || '').toLowerCase();
  const file = String(mod.file || '').toLowerCase();
  return ['fabricloader', 'fabric-loader', 'forge', 'neoforge', 'quilt_loader', 'quilt-loader', 'minecraft', 'java'].includes(id)
    || /^(fabric-loader|forge-|neoforge-|quilt-loader).*\.jar$/.test(file);
}

// A small ZIP reader is enough for JAR metadata and avoids a native dependency.
function zipEntries(buffer) {
  let eocd = -1;
  for (let i = buffer.length - 22; i >= Math.max(0, buffer.length - 0xffff - 22); i--) {
    if (buffer.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error('不是有效的 ZIP/JAR 文件');
  const count = buffer.readUInt16LE(eocd + 10);
  let at = buffer.readUInt32LE(eocd + 16);
  const entries = new Map();
  for (let n = 0; n < count && at + 46 <= buffer.length; n++) {
    if (buffer.readUInt32LE(at) !== 0x02014b50) break;
    const method = buffer.readUInt16LE(at + 10);
    const compressed = buffer.readUInt32LE(at + 20);
    const nameLength = buffer.readUInt16LE(at + 28);
    const extraLength = buffer.readUInt16LE(at + 30);
    const commentLength = buffer.readUInt16LE(at + 32);
    const localOffset = buffer.readUInt32LE(at + 42);
    const name = buffer.subarray(at + 46, at + 46 + nameLength).toString('utf8');
    entries.set(name, { method, compressed, localOffset });
    at += 46 + nameLength + extraLength + commentLength;
  }
  return { get(name) {
    const entry = entries.get(name);
    if (!entry) return null;
    const off = entry.localOffset;
    if (buffer.readUInt32LE(off) !== 0x04034b50) return null;
    const nameLength = buffer.readUInt16LE(off + 26);
    const extraLength = buffer.readUInt16LE(off + 28);
    const raw = buffer.subarray(off + 30 + nameLength + extraLength, off + 30 + nameLength + extraLength + entry.compressed);
    return entry.method === 0 ? raw : entry.method === 8 ? inflateRawSync(raw) : null;
  }};
}
function tomlValue(text, key) {
  const m = text.match(new RegExp(`^\\s*${key}\\s*=\\s*[\"']([^\"']+)[\"']`, 'mi'));
  return m?.[1]?.trim() || '';
}
function parseTomlMods(text) {
  const blocks = text.split(/^\s*\[\[mods\]\]\s*$/mi).slice(1);
  const deps = new Map();
  const depPattern = /^\s*\[\[dependencies\.([^\]]+)\]\]\s*$([\s\S]*?)(?=^\s*\[\[|\z)/gmi;
  let match;
  while ((match = depPattern.exec(text))) {
    const owner = match[1], section = match[2];
    if (/mandatory\s*=\s*false/i.test(section)) continue;
    const dependency = tomlValue(section, 'modId');
    if (dependency && !['minecraft', 'forge', 'neoforge', 'fabricloader', 'java'].includes(dependency.toLowerCase())) {
      if (!deps.has(owner)) deps.set(owner, []);
      deps.get(owner).push(dependency);
    }
  }
  return blocks.map(block => ({
    id: tomlValue(block, 'modId'), name: tomlValue(block, 'displayName'), version: tomlValue(block, 'version'), deps
  })).filter(mod => mod.id).map(mod => ({ ...mod, deps: deps.get(mod.id) || [] }));
}
function parseJarMetadata(file, buffer) {
  const zip = zipEntries(buffer);
  const read = name => zip.get(name)?.toString('utf8') || '';
  try {
    const fabric = read('fabric.mod.json');
    if (fabric) {
      const m = JSON.parse(fabric);
      return { id: m.id, name: m.name || m.id, version: m.version || '', loader: 'fabric', deps: Object.keys(m.depends || {}).filter(d => !['minecraft', 'fabricloader', 'java'].includes(d)) };
    }
    const quilt = read('quilt.mod.json');
    if (quilt) {
      const m = JSON.parse(quilt).quilt_loader || {};
      return { id: m.id, name: m.metadata?.name || m.id, version: m.version || '', loader: 'quilt', deps: Object.keys(m.depends || {}).filter(d => !['minecraft', 'quilt_loader', 'java'].includes(d)) };
    }
    const toml = read('META-INF/neoforge.mods.toml') || read('META-INF/mods.toml');
    if (toml) {
      const parsed = parseTomlMods(toml)[0];
      if (parsed) return { ...parsed, name: parsed.name || parsed.id, loader: toml.includes('neoforge') ? 'neoforge' : 'forge' };
    }
    const legacy = read('mcmod.info');
    if (legacy) {
      const data = JSON.parse(legacy); const m = Array.isArray(data) ? data[0] : data.modList?.[0];
      if (m) return { id: m.modid, name: m.name || m.modid, version: m.version || '', loader: 'forge', deps: [] };
    }
  } catch (error) { /* fall through to filename; malformed metadata is reported separately */ }
  const stem = path.basename(file, '.jar').replace(/[-_](?:mc)?\d[\w.-]*$/i, '');
  return { id: '', name: stem, version: '', loader: 'unknown', deps: [], unrecognized: true };
}
async function scanFolder(folder) {
  const files = await fs.readdir(folder, { withFileTypes: true });
  const mods = [];
  for (const entry of files) {
    if (!entry.isFile() || !/\.jar$/i.test(entry.name)) continue;
    try {
      const full = path.join(folder, entry.name);
      const stat = await fs.stat(full);
      const parsed = parseJarMetadata(entry.name, await fs.readFile(full));
      mods.push({ ...parsed, file: entry.name, size: stat.size, locked: isCore({ ...parsed, file: entry.name }) });
    } catch (error) {
      mods.push({ id: '', name: entry.name, file: entry.name, version: '', loader: 'unknown', deps: [], unrecognized: true, error: error.message, locked: false });
    }
  }
  return mods.sort((a, b) => a.name.localeCompare(b.name, 'zh-CN'));
}
async function listFilesRecursively(root, relative = '') {
  const entries = await fs.readdir(path.join(root, relative), { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const child = path.join(relative, entry.name);
    if (entry.isDirectory()) files.push(...await listFilesRecursively(root, child));
    else if (entry.isFile()) files.push(child);
  }
  return files;
}
function configBelongsToMod(relativeFile, mod) {
  const haystack = normalize(relativeFile);
  // Most mod keybinds are e.g. config/tweakeroo.json or config/malilib/*.json.
  // Do not match short IDs: that would risk copying unrelated generic configs.
  return [mod.id, mod.name].some(value => {
    const key = normalize(value);
    return key.length >= 4 && haystack.includes(key);
  });
}
async function nextBackupPath(file) {
  for (let number = 0; number < 100; number++) {
    const suffix = number ? `.migrator-backup.${number}` : '.migrator-backup';
    const candidate = `${file}${suffix}`;
    if (!fssync.existsSync(candidate)) return candidate;
  }
  throw new Error(`无法为 ${path.basename(file)} 创建配置备份`);
}
async function migrateModConfigs(sourceModsFolder, targetModsFolder, migratedMods) {
  const sourceConfig = path.join(path.dirname(sourceModsFolder), 'config');
  const targetConfig = path.join(path.dirname(targetModsFolder), 'config');
  if (!fssync.existsSync(sourceConfig) || !migratedMods.length) return { copied: [], backedUp: [], skipped: [] };
  const supported = new Set(['.json', '.toml', '.cfg', '.conf', '.properties', '.txt']);
  const files = (await listFilesRecursively(sourceConfig)).filter(file => supported.has(path.extname(file).toLowerCase()));
  const selected = files.filter(file => migratedMods.some(mod => configBelongsToMod(file, mod)));
  const outcome = { copied: [], backedUp: [], skipped: [] };
  for (const relativeFile of selected) {
    const source = path.join(sourceConfig, relativeFile);
    const target = path.join(targetConfig, relativeFile);
    const sourceBytes = await fs.readFile(source);
    if (fssync.existsSync(target)) {
      const targetBytes = await fs.readFile(target);
      if (sourceBytes.equals(targetBytes)) { outcome.skipped.push(relativeFile); continue; }
      const backup = await nextBackupPath(target);
      await fs.copyFile(target, backup);
      outcome.backedUp.push(path.relative(targetConfig, backup));
    }
    await fs.mkdir(path.dirname(target), { recursive: true });
    await fs.writeFile(target, sourceBytes);
    outcome.copied.push(relativeFile);
  }
  return outcome;
}
async function chooseFolder() {
  if (nativeFolderPicker) return nativeFolderPicker();
  if (process.platform === 'win32') {
    // Shell.Application owns the picker through Explorer rather than the Node
    // child process, so it reliably surfaces above browsers and terminal hosts.
    const script = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $shell=New-Object -ComObject Shell.Application; $folder=$shell.BrowseForFolder(0,'选择 Minecraft mods 文件夹',0x41,0); if($null -ne $folder){[Console]::Write($folder.Self.Path)}";
    const { stdout } = await execFileAsync('powershell.exe', ['-NoProfile', '-STA', '-Command', script], { windowsHide: false });
    return stdout.trim();
  }
  if (process.platform === 'darwin') {
    const script = 'POSIX path of (choose folder with prompt "选择 Minecraft mods 文件夹")';
    const { stdout } = await execFileAsync('osascript', ['-e', script]);
    return stdout.trim().replace(/\/$/, '');
  }
  throw new Error('当前系统暂不支持原生文件夹选择，请使用 Windows 或 macOS。');
}
async function modrinthCandidate(mod, gameVersion, loader) {
  const facets = [[`versions:${gameVersion}`], [`categories:${loader}`]];
  // Internal ID is more precise; display name is a valuable fallback for projects
  // whose Modrinth slug intentionally differs from their in-game ID.
  const queries = [...new Set([mod.id, mod.name].filter(Boolean))];
  const hits = [];
  for (const query of queries) {
    const params = new URLSearchParams({ query, facets: JSON.stringify(facets), limit: '20', index: 'relevance' });
    const search = await fetch(`https://api.modrinth.com/v2/search?${params}`, { headers: { 'User-Agent': 'MC-Mod-Migrator/0.1 (local utility)' } });
    if (!search.ok) throw new Error(`Modrinth 搜索失败（${search.status}）`);
    for (const hit of (await search.json()).hits || []) if (!hits.some(existing => existing.project_id === hit.project_id)) hits.push(hit);
  }
  const key = normalize(mod.id || mod.name);
  hits.sort((a, b) => Number(normalize(b.slug) === key || normalize(b.title) === key) - Number(normalize(a.slug) === key || normalize(a.title) === key));
  for (const hit of hits.filter(hit => isExactProject(hit, mod)).slice(0, 8)) {
    const versions = await fetch(`https://api.modrinth.com/v2/project/${hit.project_id}/version?loaders=${encodeURIComponent(JSON.stringify([loader]))}&game_versions=${encodeURIComponent(JSON.stringify([gameVersion]))}`, { headers: { 'User-Agent': 'MC-Mod-Migrator/0.1 (local utility)' } });
    if (!versions.ok) continue;
    const list = await versions.json();
    const release = list.find(v => v.version_type === 'release') || list[0];
    const file = release?.files?.find(f => f.primary) || release?.files?.[0];
    if (file?.url) return { source: 'Modrinth', project: hit.title, version: release.version_number, url: file.url, filename: file.filename, dependencies: release.dependencies || [] };
  }
  return null;
}
async function curseForgeCandidate(mod, gameVersion, loader, apiKey) {
  if (!apiKey) return null;
  const headers = { 'x-api-key': apiKey, 'User-Agent': 'MC-Mod-Migrator/0.1 (local utility)' };
  const searchUrl = `https://api.curseforge.com/v1/mods/search?${new URLSearchParams({ gameId: '432', searchFilter: mod.id || mod.name, pageSize: '10' })}`;
  const search = await fetch(searchUrl, { headers });
  if (!search.ok) throw new Error(`CurseForge 搜索失败（${search.status}，请检查 API Key）`);
  const key = normalize(mod.id || mod.name);
  const projects = (await search.json()).data || [];
  projects.sort((a, b) => Number(normalize(b.slug) === key || normalize(b.name) === key) - Number(normalize(a.slug) === key || normalize(a.name) === key));
  // CurseForge's API loader types: Forge=1, Fabric=4, Quilt=5, NeoForge=6.
  const loaderType = { forge: '1', fabric: '4', quilt: '5', neoforge: '6' }[loader];
  for (const project of projects.filter(project => isExactProject(project, mod)).slice(0, 6)) {
    const params = new URLSearchParams({ gameVersion, pageSize: '20' });
    if (loaderType) params.set('modLoaderType', loaderType);
    const files = await fetch(`https://api.curseforge.com/v1/mods/${project.id}/files?${params}`, { headers });
    if (!files.ok) continue;
    const releases = (await files.json()).data || [];
    const file = releases.find(f => f.downloadUrl && f.releaseType === 1) || releases.find(f => f.downloadUrl);
    if (file?.downloadUrl) return { source: 'CurseForge', project: project.name, version: file.displayName || String(file.id), url: file.downloadUrl, filename: file.fileName, dependencies: file.dependencies || [] };
  }
  return null;
}
function reportLinks(mod, gameVersion, loader) {
  const term = encodeURIComponent(`${mod.id || mod.name} ${gameVersion} ${loader}`);
  return { modrinth: `https://modrinth.com/mods?q=${term}`, curseforge: `https://www.curseforge.com/minecraft/search?search=${term}`, mcmod: `https://search.mcmod.cn/s?key=${term}` };
}
function createJob() { const id = Math.random().toString(36).slice(2); jobs.set(id, { id, status: 'running', current: '', completed: 0, total: 0, logs: [], results: [] }); return jobs.get(id); }
function log(job, type, message, mod) { job.logs.push({ type, message, mod: mod?.name || '', at: new Date().toISOString() }); }
async function saveJobLog(job) {
  await fs.mkdir(logDirectory, { recursive: true });
  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  const file = path.join(logDirectory, `migration-${stamp}.log`);
  const lines = ['MC Mod Migrator 迁移日志', `任务：${job.id}`, `状态：${job.status}`, '', ...job.logs.map(entry => `${entry.at}  ${entry.message}`)];
  await fs.writeFile(file, `${lines.join('\n')}\n`, 'utf8');
  job.logFile = file;
}
function dependencyClosure(mods, failedIds) {
  const blocked = new Set(failedIds);
  let changed = true;
  while (changed) {
    changed = false;
    for (const mod of mods) if (!blocked.has(mod.id) && mod.deps.some(dep => blocked.has(dep))) { blocked.add(mod.id); changed = true; }
  }
  return blocked;
}
async function download(url, destination) {
  const response = await fetch(url, { headers: { 'User-Agent': 'MC-Mod-Migrator/0.1 (local utility)' } });
  if (!response.ok || !response.body) throw new Error(`下载失败（${response.status}）`);
  const tmp = `${destination}.part`;
  const handle = fssync.createWriteStream(tmp);
  await new Promise((resolve, reject) => { response.body.pipeTo(new WritableStream({ write(chunk) { if (!handle.write(Buffer.from(chunk))) return new Promise(r => handle.once('drain', r)); }, close() { handle.end(resolve); }, abort(reason) { reject(reason); } })).catch(reject); handle.on('error', reject); });
  await fs.rename(tmp, destination);
}
async function runMigration(job, settings) {
  const { sourceFolder, targetFolder, gameVersion, loader, mods, excludedIds = [], curseForgeApiKey = '', migrateConfigs = true } = settings;
  const candidates = mods.filter(m => !m.locked && !m.unrecognized && !excludedIds.includes(m.id));
  job.total = candidates.length * 2 + (migrateConfigs ? 1 : 0);
  const unavailable = new Set();
  const found = new Map();
  const migratedMods = [];
  for (const mod of candidates) {
    job.current = mod.name;
    try {
      const candidate = await modrinthCandidate(mod, gameVersion, loader) || await curseForgeCandidate(mod, gameVersion, loader, curseForgeApiKey);
      if (!candidate) { unavailable.add(mod.id); log(job, 'missing', `未找到 ${mod.name} 在 ${gameVersion} / ${loader} 下的版本`, mod); }
      else { found.set(mod.id, candidate); log(job, 'found', `${mod.name} → ${candidate.project} ${candidate.version}`, mod); }
    } catch (error) { unavailable.add(mod.id); log(job, 'error', `${mod.name} 查询失败：${error.message}`, mod); }
    job.completed++;
  }
  const blocked = dependencyClosure(candidates, unavailable);
  for (const mod of candidates) {
    if (blocked.has(mod.id)) {
      const reason = unavailable.has(mod.id) ? '没有可用版本' : '依赖的模组没有可用版本';
      job.results.push({ mod, status: 'skipped', reason, links: reportLinks(mod, gameVersion, loader) });
      job.completed++;
      continue;
    }
    const candidate = found.get(mod.id);
    if (!candidate) continue;
    job.current = `下载 ${mod.name}`;
    const destination = path.join(targetFolder, candidate.filename);
    try {
      if (fssync.existsSync(destination)) log(job, 'info', `${candidate.filename} 已存在，保留现有文件`, mod);
      else await download(candidate.url, destination);
      job.results.push({ mod, status: 'migrated', file: candidate.filename, source: candidate.source, version: candidate.version });
      migratedMods.push(mod);
      log(job, 'success', `已迁移 ${mod.name}`, mod);
    } catch (error) {
      job.results.push({ mod, status: 'error', reason: error.message, links: reportLinks(mod, gameVersion, loader) });
      log(job, 'error', `${mod.name} 下载失败：${error.message}`, mod);
    }
    job.completed++;
  }
  for (const mod of mods.filter(m => m.locked)) job.results.push({ mod, status: 'locked', reason: '加载器核心文件默认不迁移' });
  for (const mod of mods.filter(m => excludedIds.includes(m.id))) job.results.push({ mod, status: 'excluded', reason: '已由你排除' });
  if (migrateConfigs) {
    job.current = '迁移模组配置与快捷键';
    try {
      job.configs = await migrateModConfigs(sourceFolder, targetFolder, migratedMods);
      const { copied, backedUp, skipped } = job.configs;
      if (copied.length) log(job, 'success', `已迁移 ${copied.length} 个模组配置/快捷键文件`);
      if (backedUp.length) log(job, 'info', `已备份 ${backedUp.length} 个目标配置文件（*.migrator-backup）`);
      if (!copied.length && !skipped.length) log(job, 'info', '没有发现可安全匹配的模组配置文件');
    } catch (error) {
      job.configs = { copied: [], backedUp: [], skipped: [], error: error.message };
      log(job, 'error', `配置迁移失败：${error.message}`);
    }
    job.completed++;
  }
  job.current = ''; job.status = 'complete';
  try { await saveJobLog(job); } catch (error) { console.error('无法保存迁移日志：', error.message); }
}

const mime = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8' };
const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, `http://${req.headers.host}`);
    if (req.method === 'GET' && url.pathname === '/') return fs.readFile(path.join(__dirname, 'web', 'index.html')).then(data => { res.writeHead(200, { 'Content-Type': mime['.html'], 'Cache-Control': 'no-store' }); res.end(data); });
    if (req.method === 'GET' && url.pathname === '/background') return fs.readFile(path.join(__dirname, 'Background.jpg')).then(data => { res.writeHead(200, { 'Content-Type': 'image/jpeg', 'Cache-Control': 'no-store' }); res.end(data); });
    if (req.method === 'GET' && url.pathname.startsWith('/assets/')) { const file = path.join(__dirname, 'web', path.basename(url.pathname)); return fs.readFile(file).then(data => { res.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'text/plain', 'Cache-Control': 'no-store' }); res.end(data); }); }
    if (req.method === 'POST' && url.pathname === '/api/choose-folder') return json(res, 200, { folder: await chooseFolder() });
    if (req.method === 'POST' && url.pathname === '/api/scan') { const { folder } = await body(req); if (!folder || !fssync.existsSync(folder)) return json(res, 400, { error: '文件夹不存在' }); return json(res, 200, { mods: await scanFolder(folder) }); }
    if (req.method === 'POST' && url.pathname === '/api/migrate') { const data = await body(req); if (!data.targetFolder || !fssync.existsSync(data.targetFolder)) return json(res, 400, { error: '目标文件夹不存在' }); if (!cleanString(data.gameVersion) || !cleanString(data.loader)) return json(res, 400, { error: '请选择目标版本与加载器' }); const job = createJob(); runMigration(job, data).catch(async error => { job.status = 'error'; job.error = error.message; log(job, 'error', `迁移失败：${error.message}`); try { await saveJobLog(job); } catch (saveError) { console.error('无法保存迁移日志：', saveError.message); } }); return json(res, 202, { id: job.id }); }
    if (req.method === 'GET' && url.pathname.startsWith('/api/job/')) { const job = jobs.get(url.pathname.split('/').pop()); return job ? json(res, 200, job) : json(res, 404, { error: '任务不存在' }); }
    json(res, 404, { error: '未找到' });
  } catch (error) { json(res, 500, { error: error.message || '服务器错误' }); }
});
let markServerReady;
const ready = new Promise(resolve => { markServerReady = resolve; });
server.listen(PORT, '127.0.0.1', () => { console.log(`MC Mod Migrator: http://127.0.0.1:${PORT}`); markServerReady(); });

module.exports = {
  ready,
  setNativeFolderPicker(picker) { nativeFolderPicker = picker; },
  setLogDirectory(directory) { if (directory) logDirectory = directory; }
};

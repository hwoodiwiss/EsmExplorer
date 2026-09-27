// Access to a user-granted game data folder via the File System Access API.
// The directory handle is held module-level (it cannot cross the JS boundary),
// and files are resolved with case-insensitive path walks so lowercase asset
// paths from plugin records match folders as they exist on disk.

let rootHandle = null;
const openFiles = new Map();
let nextFileId = 1;

export function isSupported() {
  return typeof window.showDirectoryPicker === "function";
}

export function hasRoot() {
  return rootHandle !== null;
}

export async function pickDataRoot() {
  if (!isSupported()) {
    return null;
  }
  try {
    rootHandle = await window.showDirectoryPicker({ mode: "read" });
    return rootHandle.name;
  } catch {
    // User cancelled the picker (or the browser denied it).
    return null;
  }
}

export async function resolveFile(path) {
  if (!rootHandle || !path) {
    return null;
  }
  const segments = normalizeDataPath(path)
    .split("/")
    .filter((segment) => segment.length > 0);
  if (!segments.length || segments.some(s => s === "." || s === ".." || s.includes(":"))) {
    return null;
  }
  let directory = rootHandle;
  for (let i = 0; i < segments.length - 1; i++) {
    directory = await getChild(directory, segments[i], false);
    if (!directory) {
      return null;
    }
  }
  const fileHandle = await getChild(
    directory,
    segments[segments.length - 1],
    true,
  );
  if (!fileHandle) {
    return null;
  }
  return await fileHandle.getFile();
}

export async function openFile(path) {
  const file = await resolveFile(path);
  if (!file) {
    return null;
  }
  const id = nextFileId++;
  openFiles.set(id, file);
  return { id, length: file.size };
}

export async function readFileChunk(id, offset, count) {
  const file = openFiles.get(id);
  if (!file) {
    throw new Error("File handle is closed.");
  }
  return new Uint8Array(await file.slice(offset, offset + count).arrayBuffer());
}

export function closeFile(id) {
  openFiles.delete(id);
}

export async function listFiles() {
  if (!rootHandle) {
    return [];
  }
  const files = [];
  for await (const entry of rootHandle.values()) {
    if (entry.kind === "file") {
      files.push(entry.name);
    }
  }
  return files;
}

function normalizeDataPath(path) {
  let normalized = path.replaceAll("\\", "/").replace(/^\/+/, "").toLowerCase();
  while (normalized.startsWith("data/")) {
    normalized = normalized.slice(5);
  }
  return normalized;
}

async function getChild(directory, name, isFile) {
  try {
    return isFile
      ? await directory.getFileHandle(name)
      : await directory.getDirectoryHandle(name);
  } catch {
    // Exact name miss; fall through to a case-insensitive scan.
  }
  const wanted = name.toLowerCase();
  for await (const entry of directory.values()) {
    if (
      entry.name.toLowerCase() === wanted &&
      (entry.kind === "file") === isFile
    ) {
      return entry;
    }
  }
  return null;
}

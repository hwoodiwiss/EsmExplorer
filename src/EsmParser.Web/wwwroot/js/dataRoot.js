// Access to a user-granted game data folder via the File System Access API.
// The directory handle is held module-level (it cannot cross the JS boundary),
// and files are resolved with case-insensitive path walks so lowercase asset
// paths from plugin records match folders as they exist on disk.

let rootHandle = null;

export function isSupported() {
  return typeof window.showDirectoryPicker === 'function';
}

export function hasRoot() {
  return rootHandle !== null;
}

export async function pickDataRoot() {
  if (!isSupported()) {
    return null;
  }
  try {
    rootHandle = await window.showDirectoryPicker({ mode: 'read' });
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
  const segments = normalizeDataPath(path).split('/').filter((segment) => segment.length > 0);
  let directory = rootHandle;
  for (let i = 0; i < segments.length - 1; i++) {
    directory = await getChild(directory, segments[i], false);
    if (!directory) {
      return null;
    }
  }
  const fileHandle = await getChild(directory, segments[segments.length - 1], true);
  if (!fileHandle) {
    return null;
  }
  const buffer = await (await fileHandle.getFile()).arrayBuffer();
  return new Uint8Array(buffer);
}

function normalizeDataPath(path) {
  let normalized = path.replaceAll('\\', '/').toLowerCase();
  while (normalized.startsWith('data/')) {
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
    if (entry.name.toLowerCase() === wanted && (entry.kind === 'file') === isFile) {
      return entry;
    }
  }
  return null;
}

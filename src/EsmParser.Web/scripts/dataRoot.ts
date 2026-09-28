// Access to a user-granted game data folder via the File System Access API.
// The directory handle is held module-level (it cannot cross the JS boundary),
// and files are resolved with case-insensitive path walks so lowercase asset
// paths from plugin records match folders as they exist on disk.

declare global {
  interface Window {
    showDirectoryPicker?: (options?: { mode?: "read" | "readwrite" }) => Promise<FileSystemDirectoryHandle>;
  }
}

let rootHandle: FileSystemDirectoryHandle | null = null;
const openFiles = new Map<number, File>();
let nextFileId: number = 1;

export function isSupported(): boolean {
  return typeof window.showDirectoryPicker === "function";
}

export function hasRoot(): boolean {
  return rootHandle !== null;
}

export async function pickDataRoot(): Promise<string | null> {
  const showDirectoryPicker = window.showDirectoryPicker;
  if (!showDirectoryPicker) {
    return null;
  }
  try {
    rootHandle = await showDirectoryPicker({ mode: "read" });
    return rootHandle.name;
  } catch {
    // User cancelled the picker (or the browser denied it).
    return null;
  }
}

export async function resolveFile(path: string): Promise<File | null> {
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
    const child = await getChild(directory, segments[i], "directory");
    if (!child || child.kind !== "directory") {
      return null;
    }
    directory = child;
  }
  const fileHandle = await getChild(
    directory,
    segments[segments.length - 1],
    "file",
  );
  if (!fileHandle || fileHandle.kind !== "file") {
    return null;
  }
  return await fileHandle.getFile();
}

export async function openFile(path: string): Promise<{ id: number; length: number } | null> {
  const file = await resolveFile(path);
  if (!file) {
    return null;
  }
  const id = nextFileId++;
  openFiles.set(id, file);
  return { id, length: file.size };
}

export async function readFileChunk(id: number, offset: number, count: number): Promise<Uint8Array> {
  const file = openFiles.get(id);
  if (!file) {
    throw new Error("File handle is closed.");
  }
  return new Uint8Array(await file.slice(offset, offset + count).arrayBuffer());
}

export function closeFile(id: number): void {
  openFiles.delete(id);
}

export async function listFiles(): Promise<string[]> {
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

function normalizeDataPath(path: string): string {
  let normalized = path.replaceAll("\\", "/").replace(/^\/+/, "").toLowerCase();
  while (normalized.startsWith("data/")) {
    normalized = normalized.slice(5);
  }
  return normalized;
}

async function getChild(
  directory: FileSystemDirectoryHandle,
  name: string,
  kind: FileSystemHandleKind,
): Promise<FileSystemFileHandle | FileSystemDirectoryHandle | null> {
  try {
    return kind === "file"
      ? await directory.getFileHandle(name)
      : await directory.getDirectoryHandle(name);
  } catch {
    // Exact name miss; fall through to a case-insensitive scan.
  }
  const wanted = name.toLowerCase();
  for await (const entry of directory.values()) {
    if (
      entry.name.toLowerCase() === wanted &&
      entry.kind === kind
    ) {
      return entry;
    }
  }
  return null;
}

// Random-access reads over locally-selected files without loading them into memory.
// Files are registered from an <input type="file"> element and sliced on demand,
// so multi-gigabyte plugins stay on the browser side of the JS boundary.

const files = new Map();
let nextId = 0;

export function registerAll(inputElement) {
  const selected = inputElement?.files;
  if (!selected || selected.length === 0) {
    return [];
  }
  const registered = [];
  for (const file of selected) {
    const id = ++nextId;
    files.set(id, file);
    registered.push({ id: id, name: file.name, size: file.size });
  }
  return registered;
}

export async function readSlice(id, offset, length) {
  const file = files.get(id);
  if (!file) {
    throw new Error(`No registered file with id ${id}`);
  }
  return await readFileSlice(file, offset, length);
}

export async function readFileSlice(file, offset, length) {
  const buffer = await file.slice(offset, offset + length).arrayBuffer();
  return new Uint8Array(buffer);
}

export function release(id) {
  files.delete(id);
}

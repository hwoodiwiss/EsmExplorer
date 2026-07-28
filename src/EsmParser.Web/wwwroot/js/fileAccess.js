// Random-access reads over locally-selected files without loading them into memory.
// Files are registered from an <input type="file"> element and sliced on demand,
// so multi-gigabyte plugins stay on the browser side of the JS boundary.

const files = new Map();
let nextId = 0;

export function register(inputElement) {
  const file = inputElement?.files?.[0];
  if (!file) {
    return null;
  }
  const id = ++nextId;
  files.set(id, file);
  return { id: id, name: file.name, size: file.size };
}

export async function readSlice(id, offset, length) {
  const file = files.get(id);
  if (!file) {
    throw new Error(`No registered file with id ${id}`);
  }
  const buffer = await file.slice(offset, offset + length).arrayBuffer();
  return new Uint8Array(buffer);
}

export function release(id) {
  files.delete(id);
}

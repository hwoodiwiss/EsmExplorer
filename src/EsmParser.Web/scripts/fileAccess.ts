// Random-access reads over locally-selected files without loading them into memory.
// Files are registered from an <input type="file"> element and sliced on demand,
// so multi-gigabyte plugins stay on the browser side of the JS boundary.

interface RegisteredFile {
  id: number;
  name: string;
  size: number;
}

const files = new Map<number, File>();
let nextId = 0;

export function registerAll(inputElement?: HTMLInputElement): RegisteredFile[] {
  const selected = inputElement?.files;
  if (!selected || selected.length === 0) {
    return [];
  }
  const registered: RegisteredFile[] = [];
  for (const file of selected) {
    const id = ++nextId;
    files.set(id, file);
    registered.push({ id: id, name: file.name, size: file.size });
  }
  return registered;
}

export async function readSlice(id: number, offset: number, length: number): Promise<Uint8Array> {
  const file = files.get(id);
  if (!file) {
    throw new Error(`No registered file with id ${id}`);
  }
  return await readFileSlice(file, offset, length);
}

export async function readFileSlice(file: File, offset: number, length: number): Promise<Uint8Array> {
  const buffer = await file.slice(offset, offset + length).arrayBuffer();
  return new Uint8Array(buffer);
}

export function release(id: number): void {
  files.delete(id);
}

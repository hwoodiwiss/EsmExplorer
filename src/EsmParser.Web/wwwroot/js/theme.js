// Color theme handling: persists a light/dark/auto preference and applies the
// resolved Bootstrap theme to <html data-bs-theme>. The 'auto' preference tracks
// the operating system via prefers-color-scheme.

const storageKey = 'esm-theme';
const media = window.matchMedia('(prefers-color-scheme: dark)');

function resolve(preference) {
  return preference === 'dark' || (preference === 'auto' && media.matches) ? 'dark' : 'light';
}

function apply() {
  document.documentElement.setAttribute('data-bs-theme', resolve(getPreference()));
}

export function getPreference() {
  const stored = localStorage.getItem(storageKey);
  return stored === 'light' || stored === 'dark' ? stored : 'auto';
}

export function setPreference(preference) {
  if (preference === 'light' || preference === 'dark') {
    localStorage.setItem(storageKey, preference);
  } else {
    localStorage.removeItem(storageKey);
  }
  apply();
}

media.addEventListener('change', () => {
  if (getPreference() === 'auto') {
    apply();
  }
});

apply();

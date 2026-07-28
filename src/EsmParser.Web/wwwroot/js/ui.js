// Small display helpers with no better home.

export function scrollToId(elementId) {
  document.getElementById(elementId)?.scrollIntoView({ block: 'center', behavior: 'smooth' });
}

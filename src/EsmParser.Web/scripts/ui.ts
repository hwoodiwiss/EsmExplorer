export function scrollToId(elementId: string): void {
  document.getElementById(elementId)?.scrollIntoView({ block: 'center', behavior: 'smooth' });
}

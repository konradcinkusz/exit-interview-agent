// Saves a text the person asked for. The object URL is revoked after a delay, because some browsers start the download only after
// the click has returned. Nothing is stored by this page: the file goes to the person's downloads, and the copy here ends with it.
export function downloadText(name: string, text: string, type: string): void {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 30_000);
}

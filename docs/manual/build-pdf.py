# 2026/11/08 15:20:00 UTC - PluginUI
"""Prints the Plug-in Host user manual (docs/manual/<lang>/index.html) to A4 PDF files.

Usage:  python docs/manual/build-pdf.py            (needs: pip install playwright pypdf; playwright install chromium)

Two passes: the first PDF tells on which page each chapter starts, the second one writes those page numbers in the
table of contents. Page numbers and the title go in the footer.
"""

import pathlib
import re
import sys

from playwright.sync_api import sync_playwright
from pypdf import PdfReader, PdfWriter

HERE = pathlib.Path(__file__).resolve().parent
MANUALS = {
    "fr": ("PluginHost-Manuel-fr.pdf", "Plug-in Host — Manuel d'utilisation", "Page"),
    "en": ("PluginHost-Manual-en.pdf", "Plug-in Host — User manual", "Page"),
}

FOOTER = """<div style="width:100%;font-family:Inter,'Segoe UI',sans-serif;font-size:8pt;color:#6b7385;
padding:0 16mm;display:flex;justify-content:space-between">
<span>{title}</span><span>{page} <span class="pageNumber"></span> / <span class="totalPages"></span></span></div>"""


def chapter_pages(pdf_path: pathlib.Path, chapters: list[tuple[str, str]]) -> dict[str, int]:
    """Page (1-based) where each chapter heading "<number> <title>" first appears at the top of a page."""
    pages = [(page.extract_text() or "") for page in PdfReader(str(pdf_path)).pages]
    found: dict[str, int] = {}
    for anchor, heading in chapters:
        wanted = re.sub(r"\s+", "", heading)
        for index, text in enumerate(pages):
            head = re.sub(r"\s+", "", text)[: len(wanted) + 40]
            if wanted in head:
                found[anchor] = index + 1
                break
    return found


def print_pdf(page, html: pathlib.Path, out: pathlib.Path, title: str, word: str, numbers: dict[str, int] | None) -> list[tuple[str, str]]:
    page.goto(html.as_uri(), wait_until="networkidle")
    page.evaluate("document.documentElement.setAttribute('data-theme', 'light')")
    chapters = page.evaluate("""() => [...document.querySelectorAll('h2[id]')].map(h => [h.id, h.textContent.trim()])""")
    if numbers:
        page.evaluate("""numbers => {
            for (const a of document.querySelectorAll('nav.toc a')) {
                const n = numbers[a.getAttribute('href').slice(1)];
                if (n) { const s = document.createElement('span'); s.className = 'toc-page'; s.textContent = n; a.after(s); }
            }
            const style = document.createElement('style');
            style.textContent = 'nav.toc li{display:flex;gap:8px}nav.toc li a{flex:none}' +
              'nav.toc li::after{content:"";order:1;flex:1;border-bottom:1px dotted #aab;margin-bottom:6px}' +
              '.toc-page{order:2;flex:none}';
            document.head.appendChild(style);
        }""", numbers)
    page.pdf(path=str(out), format="A4", print_background=True, prefer_css_page_size=True,
             display_header_footer=True, header_template="<span></span>",
             footer_template=FOOTER.format(title=title, page=word),
             margin={"top": "18mm", "bottom": "20mm", "left": "16mm", "right": "16mm"})
    return chapters


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page()
        for lang, (name, title, word) in MANUALS.items():
            html = HERE / lang / "index.html"
            if not html.exists():
                continue
            out = HERE / lang / name
            chapters = print_pdf(page, html, out, title, word, None)
            numbers = chapter_pages(out, [(a, t) for a, t in chapters if a])
            print_pdf(page, html, out, title, word, numbers)
            # Document properties (title, author) for readers and search engines.
            reader = PdfReader(str(out))
            writer = PdfWriter(clone_from=reader)
            writer.add_metadata({"/Title": title, "/Author": "asm8086", "/Subject": "PluginUI — Plug-in Host"})
            with open(out, "wb") as f:
                writer.write(f)
            print(f"{out.relative_to(HERE.parent.parent)}: {len(reader.pages)} pages, chapters on pages {numbers}")
        browser.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())

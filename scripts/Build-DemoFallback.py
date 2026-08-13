from pathlib import Path
from textwrap import wrap
import re
import xml.etree.ElementTree as ET

from reportlab.lib import colors
from reportlab.lib.pagesizes import landscape
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfgen import canvas
from pypdf import PdfReader, PdfWriter


ROOT = Path(__file__).resolve().parent.parent
VERSION = ET.parse(ROOT / "Sati.csproj").findtext(".//Version")
if VERSION is None or re.fullmatch(r"\d+\.\d+\.\d+", VERSION) is None:
    raise ValueError("Sati.csproj must contain a semantic Version such as 1.2.1")
OUT = ROOT / "output" / "pdf" / f"Sati-Company-Demo-Fallback-{VERSION}.pdf"
PAGE = landscape((7.5 * 72, 13.333 * 72))
W, H = PAGE

INK = colors.HexColor("#2E2824")
MUTED = colors.HexColor("#6F6760")
COPPER = colors.HexColor("#B86735")
COPPER_DARK = colors.HexColor("#8C4F2D")
CREAM = colors.HexColor("#FBF7F1")
SAND = colors.HexColor("#E8D5C1")
GREEN = colors.HexColor("#5F775F")
GREEN_PALE = colors.HexColor("#E6EEE4")
BLUE = colors.HexColor("#4E6D80")
WHITE = colors.white

font_regular = Path(r"C:\Windows\Fonts\segoeui.ttf")
font_bold = Path(r"C:\Windows\Fonts\segoeuib.ttf")
font_serif = Path(r"C:\Windows\Fonts\cambria.ttc")
pdfmetrics.registerFont(TTFont("SatiSans", str(font_regular)))
pdfmetrics.registerFont(TTFont("SatiSans-Bold", str(font_bold)))
pdfmetrics.registerFont(TTFont("SatiSerif", str(font_serif), subfontIndex=0))


def page_frame(c, number, label="OFFLINE COMPANY DEMO FALLBACK"):
    c.setFillColor(CREAM)
    c.rect(0, 0, W, H, fill=1, stroke=0)
    c.setFillColor(MUTED)
    c.setFont("SatiSans-Bold", 7.5)
    c.drawString(42, H - 28, label)
    c.setStrokeColor(SAND)
    c.line(42, H - 35, W - 42, H - 35)
    c.setFont("SatiSans", 7)
    c.drawString(42, 20, f"Satilogica | Synthetic records only | Sati {VERSION}")
    c.drawRightString(W - 42, 20, str(number))


def title(c, kicker, heading, sub=None):
    c.setFillColor(COPPER_DARK)
    c.setFont("SatiSans-Bold", 9)
    c.drawString(52, H - 70, kicker.upper())
    c.setFillColor(INK)
    c.setFont("SatiSerif", 27)
    c.drawString(52, H - 108, heading)
    c.setStrokeColor(COPPER)
    c.setLineWidth(1)
    c.line(52, H - 120, W - 52, H - 120)
    if sub:
        c.setFillColor(MUTED)
        c.setFont("SatiSans", 10)
        c.drawString(52, H - 140, sub)


def paragraph(c, text, x, y, width_chars=88, size=11, leading=16, color=INK, bold=False):
    c.setFillColor(color)
    c.setFont("SatiSans-Bold" if bold else "SatiSans", size)
    for line in wrap(text, width=width_chars):
        c.drawString(x, y, line)
        y -= leading
    return y


def bullet_list(c, items, x, y, width_chars=72, size=11, leading=16, gap=8):
    for item in items:
        lines = wrap(item, width=width_chars)
        c.setFillColor(COPPER)
        c.circle(x + 4, y + 3, 2.5, fill=1, stroke=0)
        c.setFillColor(INK)
        c.setFont("SatiSans", size)
        for index, line in enumerate(lines):
            c.drawString(x + 16, y - index * leading, line)
        y -= len(lines) * leading + gap
    return y


def rounded_box(c, x, y, w, h, heading, body, fill=WHITE):
    c.setFillColor(fill)
    c.setStrokeColor(SAND)
    c.roundRect(x, y, w, h, 8, fill=1, stroke=1)
    c.setFillColor(COPPER_DARK)
    c.setFont("SatiSans-Bold", 10)
    c.drawString(x + 12, y + h - 20, heading)
    paragraph(c, body, x + 12, y + h - 40, width_chars=max(24, int(w / 7.2)), size=8.5, leading=12, color=INK)


def fit_image(c, path, x, y, w, h):
    image = ImageReader(str(path))
    iw, ih = image.getSize()
    scale = min(w / iw, h / ih)
    dw, dh = iw * scale, ih * scale
    c.setFillColor(WHITE)
    c.setStrokeColor(SAND)
    c.roundRect(x - 6, y - 6, w + 12, h + 12, 8, fill=1, stroke=1)
    c.drawImage(image, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh, preserveAspectRatio=True, mask="auto")


def arrow(c, x1, y1, x2, y2):
    c.setStrokeColor(COPPER)
    c.setFillColor(COPPER)
    c.setLineWidth(2)
    c.line(x1, y1, x2, y2)
    c.line(x2, y2, x2 - 8, y2 + 5)
    c.line(x2, y2, x2 - 8, y2 - 5)


def build():
    OUT.parent.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(OUT), pagesize=PAGE, invariant=1)
    c.setTitle("Sati Company Demo Fallback")
    c.setAuthor("Satilogica")
    c.setSubject("Offline company demonstration fallback using synthetic records")

    # 1 - cover
    c.setFillColor(INK)
    c.rect(0, 0, W, H, fill=1, stroke=0)
    c.setFillColor(COPPER)
    c.circle(86, H - 96, 24, fill=1, stroke=0)
    c.setFillColor(WHITE)
    c.setFont("SatiSerif", 38)
    c.drawString(126, H - 108, "Sati")
    c.setFont("SatiSans-Bold", 13)
    c.drawString(64, H - 190, "COMPANY DEMONSTRATION")
    c.setFont("SatiSerif", 34)
    c.drawString(64, H - 242, "Human-services work, made visible")
    c.setStrokeColor(COPPER)
    c.line(64, H - 260, W - 64, H - 260)
    c.setFont("SatiSans", 14)
    c.drawString(64, H - 298, f"Offline fallback for the Sati {VERSION} synthetic demonstration")
    c.setFillColor(colors.HexColor("#C8C1BA"))
    c.setFont("SatiSans", 9)
    c.drawString(64, 66, "Use only if the live demonstration is unavailable. No real client data appears here.")
    c.drawRightString(W - 64, 66, "August 2026")
    c.showPage()

    # 2 - orientation
    page_frame(c, 2)
    title(c, "Start here", "What this fallback is - and is not")
    rounded_box(c, 52, 250, 400, 150, "It is", "A calm, offline visual tour of the current product direction and representative synthetic workflows. It keeps the conversation moving when a network or cloud service is unavailable.", GREEN_PALE)
    rounded_box(c, 478, 250, 430, 150, "It is not", "A recording of live production data, a substitute for authenticated acceptance testing, a claim of payer certification, or proof that commercial-production controls are complete.", colors.HexColor("#F6E9E1"))
    c.setFillColor(INK)
    c.setFont("SatiSans-Bold", 12)
    c.drawString(52, 210, "Presenter promises")
    bullet_list(c, [
        "Say that every person, provider, note, and identifier shown is synthetic.",
        "Use the fallback to explain workflows; do not imply the screens are connected live.",
        "Distinguish company-demo readiness from commercial-production readiness.",
        "Explain that identity, external monitoring, legal-hold enforcement, recovery drills, and payer acceptance remain production work.",
    ], 56, 184, width_chars=100, size=10, leading=14, gap=6)
    c.showPage()

    # 3 - architecture
    page_frame(c, 3)
    title(c, "Product shape", "One guarded route to each agency's records", "API means application programming interface: the server doorway through which clients request work.")
    rounded_box(c, 60, 265, 170, 90, "Windows client", "The current Windows Presentation Foundation staff application presents workflows.", GREEN_PALE)
    rounded_box(c, 60, 145, 170, 90, "Future clients", "Web and mobile clients can use the same guarded server boundary later.", GREEN_PALE)
    rounded_box(c, 360, 205, 210, 110, "HTTPS API", "The encrypted server validates identity, agency ownership, permissions, revisions, and workflow rules.", colors.HexColor("#F4E8DD"))
    rounded_box(c, 700, 265, 190, 90, "Agency identity", "Each signed-in person carries an agency and role that the server rechecks.", WHITE)
    rounded_box(c, 700, 145, 190, 90, "Azure SQL database", "Structured records, audit events, and version history remain behind the server.", WHITE)
    arrow(c, 235, 310, 350, 260)
    arrow(c, 235, 190, 350, 260)
    arrow(c, 580, 260, 690, 310)
    arrow(c, 580, 260, 690, 190)
    c.setFillColor(MUTED)
    c.setFont("SatiSans-Bold", 9)
    c.drawCentredString(W / 2, 92, "A distributed client never receives a shared cloud database password.")
    c.showPage()

    # 4 - working day
    page_frame(c, 4)
    title(c, "Case manager", "The working day in one place", "Representative earlier interface capture; all names and content are synthetic.")
    fit_image(c, ROOT / "images" / "Screenshots" / "mainwindow.png", 72, 98, 816, 282)
    c.setFillColor(MUTED)
    c.setFont("SatiSans", 8.5)
    c.drawCentredString(W / 2, 68, "Narrative entry, note status, upcoming compliance work, schedule, and monthly productivity share one workspace.")
    c.showPage()

    # 5 - person-centered record
    page_frame(c, 5)
    title(c, "Person record", "Context without losing the person", "Representative earlier interface capture; the fictional names are drawn from demonstration data.")
    fit_image(c, ROOT / "images" / "Screenshots" / "client_list.png", 180, 58, 600, 295)
    c.showPage()

    # 6 - admin
    page_frame(c, 6)
    title(c, "Administrator", "Operational visibility without opening Azure")
    tiles = [
        ("Agency usage", "Users, active accounts, case managers, People, and recent sign-ins."),
        ("System status", "Database health, retained audit activity, billing replay files, and policy mode."),
        ("Protected activity", "A 30-day feed of meaningful actions, actors, times, and resources."),
        ("Person directory", "Agency-scoped access to a Person's lifecycle history and auditor export."),
    ]
    positions = [(52, 280), (500, 280), (52, 145), (500, 145)]
    for (heading, body), (x, y) in zip(tiles, positions):
        rounded_box(c, x, y, 410, 105, heading, body, WHITE if x > 100 else GREEN_PALE)
    c.setFillColor(MUTED)
    c.setFont("SatiSans", 9)
    c.drawCentredString(W / 2, 102, "The live application enforces the Admin role and agency boundary on the server, not merely in the menu.")
    c.showPage()

    # 7 - audit
    page_frame(c, 7)
    title(c, "Audit history", "A Person's story remains traceable")
    stages = [
        ("Created", "Initial snapshot"),
        ("Profile edited", "Changed fields"),
        ("Journal edited", "New revision"),
        ("History viewed", "Access audited"),
        ("PDF exported", "Auditor copy"),
    ]
    x = 55
    for index, (heading, body) in enumerate(stages):
        rounded_box(c, x, 245, 150, 100, heading, body, GREEN_PALE if index in (0, 4) else WHITE)
        if index < len(stages) - 1:
            arrow(c, x + 153, 295, x + 178, 295)
        x += 185
    c.setFillColor(INK)
    c.setFont("SatiSans-Bold", 11)
    c.drawString(55, 190, "Every preserved revision answers four questions")
    bullet_list(c, [
        "What changed? A field-level change list and compressed snapshot preserve the state.",
        "Who changed it? The authenticated actor is recorded.",
        "When did it change? Coordinated Universal Time gives a consistent timestamp.",
        "Which agency owns it? Server-side tenant checks keep one agency from seeing another's history.",
    ], 58, 162, width_chars=106, size=10, leading=14, gap=5)
    c.showPage()

    # 8 - safety
    page_frame(c, 8)
    title(c, "Safety layers", "Harder to lose work, cross boundaries, or repeat money")
    rounded_box(c, 52, 290, 270, 115, "Tenant isolation", "Every protected route derives the agency from the signed-in actor and rejects cross-agency records.", GREEN_PALE)
    rounded_box(c, 345, 290, 270, 115, "Conflict protection", "Revision numbers stop an older open editor from silently replacing a newer saved record.", WHITE)
    rounded_box(c, 638, 290, 270, 115, "Retry safety", "Stable request keys prevent an ambiguous billing retry from creating a second successful file or state change.", GREEN_PALE)
    rounded_box(c, 52, 135, 270, 115, "Calm errors", "People see a reference number; protected content and full exception messages stay out of ordinary dialogs.", WHITE)
    rounded_box(c, 345, 135, 270, 115, "Canonical reset", "The approved synthetic local database can be checksum-verified and restored after rehearsal mutations.", GREEN_PALE)
    rounded_box(c, 638, 135, 270, 115, "Controlled release", "The installer is hashed, versioned, checked for private settings, and exercised before presentation.", WHITE)
    c.showPage()

    # 9 - honest boundary
    page_frame(c, 9)
    title(c, "Honest boundary", "Strong company demo; not yet broad commercial production")
    rounded_box(c, 52, 165, 410, 230, "Ready to demonstrate", "Agency-scoped workflows; synthetic case-management records; Admin usage and operations visibility; Person lifecycle history and auditor PDF; concurrency protection; billing retry safety; versioned installer; canonical reset; release notes; and evidence-driven acceptance.", GREEN_PALE)
    rounded_box(c, 498, 165, 410, 230, "Still production work", "Modern workforce identity and multifactor authentication; implemented legal hold and retention jobs; external alerts and on-call response; timed cloud restore drills; code signing; accessibility and performance evidence; payer or clearinghouse acceptance; contracts, support, and pilot operations.", colors.HexColor("#F6E9E1"))
    c.setFillColor(MUTED)
    c.setFont("SatiSans-Bold", 9)
    c.drawCentredString(W / 2, 112, "A polished demonstration is evidence of product direction - not a shortcut around production controls.")
    c.showPage()

    # 10 - close and approval
    page_frame(c, 10)
    title(c, "Close", "What an agency can evaluate today")
    bullet_list(c, [
        "Whether the workflow reflects how case managers, supervisors, and administrators actually work.",
        "Whether the interface makes documentation, compliance obligations, and operational status easier to understand.",
        "Whether the security and audit direction is credible enough to justify a controlled production pilot after the remaining gates.",
        "Which agency-specific workflows, reports, integrations, and onboarding needs belong in pilot discovery.",
    ], 58, 370, width_chars=105, size=11, leading=16, gap=9)
    c.setFillColor(COPPER_DARK)
    c.setFont("SatiSans-Bold", 11)
    c.drawString(58, 230, "Fallback approval before the meeting")
    checks = [
        "[  ] Exact PDF reviewed offline",
        "[  ] Synthetic-data statement rehearsed",
        "[  ] Product limitations stated plainly",
        "[  ] Live failure transition practiced",
    ]
    c.setFillColor(INK)
    c.setFont("SatiSans", 10)
    for index, item in enumerate(checks):
        c.drawString(62 + (index % 2) * 380, 200 - (index // 2) * 34, item)
    c.setStrokeColor(SAND)
    c.line(58, 105, 390, 105)
    c.line(520, 105, 900, 105)
    c.setFillColor(MUTED)
    c.setFont("SatiSans", 8)
    c.drawString(58, 92, "Presenter / approver")
    c.drawString(520, 92, "Date and meeting")
    c.showPage()

    c.save()

    reader = PdfReader(str(OUT))
    writer = PdfWriter()
    writer.clone_document_from_reader(reader)
    writer.add_metadata({
        "/Title": "Sati Company Demo Fallback",
        "/Author": "Satilogica",
        "/Subject": "Offline company demonstration fallback using synthetic records",
        "/Keywords": "Sati, demo, synthetic, fallback, case management",
    })
    with OUT.open("wb") as stream:
        writer.write(stream)

    print(f"PDF={OUT}")
    print(f"PAGES={len(PdfReader(str(OUT)).pages)}")
    print(f"BYTES={OUT.stat().st_size}")


if __name__ == "__main__":
    build()

#!/usr/bin/env python3
"""Generate Axon's offline Windows operator PDF guides."""

from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import inch
from reportlab.platypus import (
    BaseDocTemplate, Frame, KeepTogether, PageBreak, PageTemplate, Paragraph,
    Spacer, Table, TableStyle,
)


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "output" / "pdf"
NAVY = colors.HexColor("#17243A")
BLUE = colors.HexColor("#2878C8")
PALE = colors.HexColor("#EAF3FB")
INK = colors.HexColor("#202936")
MUTED = colors.HexColor("#667085")

styles = getSampleStyleSheet()
styles.add(ParagraphStyle(name="AxonTitle", parent=styles["Title"], fontName="Helvetica-Bold", fontSize=24, leading=29, textColor=NAVY, spaceAfter=8))
styles.add(ParagraphStyle(name="AxonSubtitle", parent=styles["Normal"], fontSize=11, leading=15, textColor=MUTED, spaceAfter=18))
styles.add(ParagraphStyle(name="H1x", parent=styles["Heading1"], fontName="Helvetica-Bold", fontSize=16, leading=20, textColor=NAVY, spaceBefore=8, spaceAfter=8))
styles.add(ParagraphStyle(name="H2x", parent=styles["Heading2"], fontName="Helvetica-Bold", fontSize=11.5, leading=15, textColor=BLUE, spaceBefore=8, spaceAfter=5))
styles.add(ParagraphStyle(name="Bodyx", parent=styles["BodyText"], fontSize=9.4, leading=13.2, textColor=INK, spaceAfter=6))
styles.add(ParagraphStyle(name="Bulletx", parent=styles["BodyText"], fontSize=9.2, leading=12.8, textColor=INK, leftIndent=13, firstLineIndent=-8, bulletIndent=3, spaceAfter=3))
styles.add(ParagraphStyle(name="BulletBody", parent=styles["BodyText"], fontSize=9.2, leading=12.8, textColor=INK))
styles.add(ParagraphStyle(name="CodeBlock", parent=styles["Code"], fontName="Courier", fontSize=7.7, leading=10.5, textColor=INK, backColor=colors.HexColor("#F3F5F7"), borderPadding=7, spaceBefore=3, spaceAfter=8))
styles.add(ParagraphStyle(name="Callout", parent=styles["BodyText"], fontSize=9.3, leading=13, textColor=NAVY, backColor=PALE, borderColor=BLUE, borderWidth=0.6, borderPadding=8, spaceBefore=5, spaceAfter=9))
styles.add(ParagraphStyle(name="Cell", parent=styles["BodyText"], fontSize=8.3, leading=11, textColor=INK))
styles.add(ParagraphStyle(name="CellHead", parent=styles["BodyText"], fontName="Helvetica-Bold", fontSize=8.3, leading=11, textColor=colors.white))


def p(text, style="Bodyx"):
    return Paragraph(escape(text).replace("\n", "<br/>"), styles[style])


def rich(text, style="Bodyx"):
    return Paragraph(text, styles[style])


def bullets(items):
    rows = [[p("-", "BulletBody"), p(item, "BulletBody")] for item in items]
    result = Table(rows, colWidths=[0.18*inch, 6.88*inch], hAlign="LEFT")
    result.setStyle(TableStyle([
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 0),
        ("RIGHTPADDING", (0, 0), (0, -1), 3),
        ("RIGHTPADDING", (1, 0), (1, -1), 0),
        ("TOPPADDING", (0, 0), (-1, -1), 1),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
    ]))
    return [result]


def code(text):
    return p(text, "CodeBlock")


def table(rows, widths):
    cooked = []
    for row_index, row in enumerate(rows):
        style = "CellHead" if row_index == 0 else "Cell"
        cooked.append([p(str(value), style) for value in row])
    result = Table(cooked, colWidths=widths, repeatRows=1, hAlign="LEFT")
    result.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), NAVY),
        ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#CAD2DC")),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 6),
        ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#F7F9FB")]),
    ]))
    return result


class GuideDoc(BaseDocTemplate):
    def __init__(self, path, title):
        super().__init__(str(path), pagesize=letter, rightMargin=0.62*inch, leftMargin=0.62*inch, topMargin=0.62*inch, bottomMargin=0.58*inch, title=title, author="Axon Project")
        self.guide_title = title
        frame = Frame(self.leftMargin, self.bottomMargin, self.width, self.height, id="body")
        self.addPageTemplates(PageTemplate(id="guide", frames=frame, onPage=self.decorate))

    def decorate(self, canvas, doc):
        canvas.saveState()
        canvas.setStrokeColor(colors.HexColor("#D7DDE5"))
        canvas.line(doc.leftMargin, 0.43*inch, letter[0]-doc.rightMargin, 0.43*inch)
        canvas.setFont("Helvetica", 7.5)
        canvas.setFillColor(MUTED)
        canvas.drawString(doc.leftMargin, 0.25*inch, self.guide_title)
        canvas.drawRightString(letter[0]-doc.rightMargin, 0.25*inch, f"Page {doc.page}")
        canvas.restoreState()


def cover(title, subtitle):
    return [
        Spacer(1, 0.35*inch), p("AXON", "H2x"), p(title, "AxonTitle"), p(subtitle, "AxonSubtitle"),
        rich("<b>v0.1.0 Offline Release</b> &nbsp; | &nbsp; Windows 11 &nbsp; | &nbsp; July 2026", "Bodyx"),
        Spacer(1, 0.12*inch),
        table([
            ["Item", "Value"],
            ["Homeserver URL", "Environment-specific (POC: http://192.168.0.113)"],
            ["Matrix identity", "axon.home.arpa"],
            ["Client networks", "Flat LAN or routed/NATed private subnets"],
            ["Host exposure", "TCP 80 only"],
        ], [1.55*inch, 4.7*inch]),
        Spacer(1, 0.18*inch),
        p("Axon provides text messaging through stock Element or Element X. Internet, cellular data, federation, media, calling, and internet push notifications are outside the initial proof of concept.", "Callout"),
    ]


def build_setup():
    story = cover("Windows Setup Guide", "Offline Docker Desktop installation, safe NIC handling, validation, repair, and recovery")
    story += [PageBreak(), p("1. Prepare Windows and the LAN", "H1x")]
    story += bullets([
        "Windows 11 x64 build 22631 or newer with an administrator account.",
        "Hardware virtualization enabled in UEFI/BIOS.",
        "Docker supports an 8 GiB RAM baseline; 16 GiB RAM and 100 GiB free disk are recommended for the planned 200-user deployment. Capacity warnings do not block a normal Axon installation.",
        "The Razer 18 with 32 GiB RAM and SSD/NVMe storage is comfortably suitable.",
        "Use a connected physical Ethernet or Wi-Fi adapter and extract the complete ZIP to a local NTFS drive.",
        "Confirm the organization is authorized to accept and use Docker Desktop; some enterprise and government use requires a paid subscription.",
    ])
    story += [p("Proven routed/NAT example", "H2x"), table([
        ["Device", "Example", "Role"],
        ["Upstream gateway", "192.168.0.1/24", "Ubiquiti LAN gateway"],
        ["Axon host", "192.168.0.113/24", "Stable or DHCP-reserved Windows address"],
        ["Main network gateway", "WAN DHCP; LAN 10.77.0.1", "Routes/NATs or forwards client traffic"],
        ["Downstream routers", "Static transit addresses", "May provide their own client DHCP/NAT"],
    ], [1.6*inch, 1.9*inch, 2.75*inch])]
    story += [p("Record the Axon IP, adapter, gateway WAN/LAN addresses, downstream client CIDRs, Element-facing URL, and the source addresses Windows sees. Preserve the correct Windows NIC configuration by default.", "Callout")]
    story += [PageBreak(), p("2. Verify and install", "H1x"), code("1. Extract Axon-v0.1.0-offline-win-x64.zip completely.\n2. Double-click Install Axon.cmd.\n3. Approve the Windows administrator prompt.")]
    story += bullets([
        "The launcher bypasses PowerShell policy only for its own process. It does not change the machine policy.",
        "Strict checksums run automatically before bundled installers or images. A failed hash normally means the ZIP must be copied again.",
        "If Windows features, WSL, or Docker request a restart, restart and double-click Install Axon.cmd again.",
        "The installer lists connected adapters and IPv4 addresses. The default NicMode Preserve does not alter the selected address, gateway, or DNS.",
        "Create the first Matrix server administrator when prompted. Use a unique operational password of at least 12 characters.",
    ])
    story += [p("Routed source scopes", "H2x"), code('.\\scripts\\Install-Axon.ps1 -BindIp 192.168.0.113 -InterfaceAlias "Ethernet" -AllowedRemoteAddress "10.77.0.0/24","10.88.0.0/24"')]
    story += [p("Only use NicMode Configure when Axon is explicitly authorized to change the adapter. It adds and verifies the new address before removing older IPv4 addresses.", "Callout")]
    story += [PageBreak(), p("3. Validate the running stack", "H1x"), code("powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\Test-Axon.ps1")]
    story += bullets([
        "postgres, synapse, and gateway must all report healthy.",
        "%ProgramData%\\Axon\\runtime\\synapse\\homeserver.yaml must exist and be non-empty.",
        "http://192.168.0.113/_matrix/client/versions (replace with the selected IP) must return Matrix JSON.",
        "Ports 8008, 8780, and 5432 must not be reachable through the Axon LAN address.",
        "Axon Control must answer only at http://127.0.0.1:8780.",
    ])
    story += [p("4. Cold start and recovery", "H1x")]
    story += bullets([
        "After Windows restart, start Docker Desktop and wait for its Linux engine. The axon group normally starts automatically.",
        "Open the Axon Control Desktop shortcut. If it does not load, run Axon Control Panel from Task Scheduler.",
        "If GUI login reports Synapse unavailable, start the complete axon group in Docker Desktop, then refresh.",
        "For an interrupted install, double-click Install Axon.cmd again. Existing runtime secrets and data are preserved.",
    ])
    story += [p("Repair command", "H2x"), code("powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\Repair-Axon.ps1")]
    story += [p("Do not delete %ProgramData%\\Axon or Docker volumes while diagnosing a failure. Repair preserves secrets and PostgreSQL data.", "Callout")]
    GuideDoc(OUTPUT / "Axon_Windows_Setup_Guide.pdf", "Axon Windows Setup Guide").build(story)


def build_client():
    story = cover("Client Connection Guide", "Connect stock Element or Element X and complete the first encrypted messaging test")
    story += [PageBreak(), p("1. Reach Axon", "H1x")]
    story += bullets([
        "Install Element or Element X from an approved store or sideload source.",
        "Join the intended router/client network and obtain a unique address from that router's DHCP server.",
        "Disable AP/client isolation and confirm the environment-specific Axon address is reachable through the intended route, NAT, or forward.",
        "No internet, cellular data, public DNS, public certificate, or push service is required for the initial test.",
    ])
    story += [code("# Replace with the Axon address visible to this client\nTest-NetConnection 192.168.0.113 -Port 80\ncurl.exe --noproxy \"*\" http://192.168.0.113/_matrix/client/versions")]
    story += [p("2. Sign in", "H1x")]
    story += bullets([
        "Choose Sign in, then Change or Edit homeserver.",
        "Enter the operator-issued URL exactly, for example http://192.168.0.113.",
        "Enter the administrator-created username and password. The full identity is @username:axon.home.arpa.",
        "Accept any client warning that internet features or push notifications are unavailable; do not substitute matrix.org.",
        "After first login, change any issued stock password in account/security settings. Axon requires at least 10 characters for GUI-issued passwords.",
    ])
    story += [p("HTTP compatibility varies by client build and mobile operating-system policy. Test the current stock applications first. If a build refuses cleartext HTTP, record the exact app version and error; the planned fallback is private-CA TLS, not public internet TLS.", "Callout")]
    story += [PageBreak(), p("3. Two-client messaging test", "H1x")]
    story += bullets([
        "Sign in as separate users on Client A and Client B.",
        "Create a private encrypted room, invite the other Axon identity, and accept the invitation.",
        "Send A to B, B to A, then disconnect B and send another message from A.",
        "Reconnect B within 48 hours and confirm synchronization.",
        "Verify unrelated application traffic independently. Axon only hosts its documented Matrix services.",
    ])
    story += [p("Notification expectation", "H2x"), p("Synapse push processing is disabled in v0.1.0. Messages synchronize when the client is active or reconnects. Axon purges eligible server-side history after the configured lifetime; E2EE hides plaintext but does not eliminate temporary encrypted event storage.", "Bodyx")]
    story += [p("Troubleshooting order", "H2x"), table([
        ["Symptom", "Check"],
        ["No TCP connection", "Client subnet, isolation, Windows NIC, firewall, and link state"],
        ["TCP succeeds, URL fails", "Run Matrix versions URL; confirm all three containers are healthy"],
        ["URL accepted, login spins", "Synapse logs, exact credentials, and client HTTP policy"],
        ["No background alert", "Expected without internet push; reopen app and resynchronize"],
    ], [1.7*inch, 4.55*inch])]
    GuideDoc(OUTPUT / "Axon_Client_Connection_Guide.pdf", "Axon Client Connection Guide").build(story)


def build_maintenance():
    story = cover("Maintenance and Tuning Guide", "Safe changes for the offline Windows 11 Matrix deployment")
    story += [PageBreak(), p("1. Routine operations", "H1x"), code(".\\scripts\\Test-Axon.ps1 -BindIp 192.168.0.113\n# Replace the example IP with the selected Windows address")]
    story += [p("Host-only Axon Control", "H2x"), code("http://127.0.0.1:8780")]
    story += bullets([
        "Sign in with a Matrix server-administrator account; credentials are not stored.",
        "Create individual or 1-200 account batches with stock passwords and standard/admin roles.",
        "Download the issued-account CSV, distribute it privately, and delete it when no longer needed.",
        "Reset passwords, promote/demote, lock/unlock, and review honest last-seen activity categories.",
        "Inspect container health and CPU/memory use; start, stop, or restart a service or the full stack.",
        "Search rooms, create private encrypted rooms, inspect membership, and add or remove local users.",
        "Read bounded PostgreSQL, Synapse, and nginx logs.",
        "The nginx gateway blocks external Synapse admin paths; ports 8008 and 8780 remain loopback-only.",
    ])
    story += [p("Room administration", "H2x")]
    story += bullets([
        "Axon-created rooms are private, E2EE-enabled, and non-federated.",
        "When changing membership in a user-created room, Axon visibly joins the signed-in administrator and grants room-level control. It does not silently impersonate another user.",
        "Delete and purge requires the exact room name, removes local members, blocks rejoining, and starts an asynchronous Synapse purge.",
        "Activity categories use last-seen timestamps. Presence is disabled, so they are not exact online/offline indicators.",
    ])
    story += [p("Use whole-stack controls for routine work. Stopping PostgreSQL interrupts Synapse, stopping Synapse interrupts messaging, and stopping nginx removes client ingress. Pause all services preserves configuration, users, rooms, and volumes.", "Callout")]
    story += [Spacer(1, 0.08*inch), p("Protected state", "H2x"), p("%ProgramData%\\Axon contains .env, homeserver.yaml, nginx configuration, secrets, and installer state. Docker volumes contain PostgreSQL and Synapse data. Back up before editing and never share these files.", "Callout")]
    story += [p("2. Message retention", "H1x")]
    story += bullets([
        "Axon defaults to a 48-hour maximum lifetime with periodic purge.",
        "E2EE prevents Synapse from reading plaintext, but encrypted events and routing metadata are temporarily stored so offline recipients can synchronize.",
        "A 48-hour policy is not a guaranteed exact deletion timestamp; purge jobs run on an interval.",
        "Changing retention affects future server behavior and must be tested with disposable accounts before operational use.",
    ])
    story += [code("Copy-Item $env:ProgramData\\Axon\\runtime\\synapse\\homeserver.yaml `\n  $env:ProgramData\\Axon\\runtime\\synapse\\homeserver.yaml.bak\n# Edit retention values, restart Synapse in Axon Control, then:\n.\\scripts\\Test-Axon.ps1 -BindIp 192.168.0.113")]
    story += [PageBreak(), p("3. Resource tuning", "H1x")]
    story += bullets([
        "For the 200-user messaging POC, 4 or more modern CPU cores, 16 GiB RAM, and SSD/NVMe storage are recommended. The 32 GiB Razer provides useful headroom.",
        "Docker Desktop with WSL 2 shares Windows resources dynamically. Do not impose low WSL limits before collecting real usage data.",
        "Low RAM or disk produces an advisory warning during normal installation rather than blocking it. Record the warning and reduce the expected capacity.",
        "Keep 100 GiB free as the operational target and monitor any host below 20 GiB closely during image extraction and soak testing.",
        "Use Docker Desktop statistics and Windows Task Manager during a soak test; tune only after observing sustained pressure.",
    ])
    story += [p("Safe change sequence", "H2x"), table([
        ["Step", "Action"],
        ["1", "Copy the exact configuration file and record current container health"],
        ["2", "Make one change; never edit secrets or immutable image references casually"],
        ["3", "Restart only the affected service and run Test-Axon.ps1"],
        ["4", "Exercise two-client messaging and offline/reconnect delivery"],
        ["5", "Restore the backup immediately if health or messaging regresses"],
    ], [0.55*inch, 5.7*inch])]
    story += [Spacer(1, 0.08*inch), p("Release-level changes", "H2x"), p("Treat a Matrix identity-domain change, TLS/private CA, media, calling, federation, image upgrade, or nginx exposure as a planned release. Address/subnet changes require coordinated NIC, Compose, firewall, route/forward, and client-URL updates.", "Callout")]
    story += [p("4. Recovery and removal", "H1x"), code('powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\Repair-Axon.ps1\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\Uninstall-Axon.ps1')]
    story += bullets([
        "Repair is idempotent and restores Synapse volume ownership without deleting data.",
        "Normal uninstall stops services and removes Axon firewall rules while preserving data.",
        "Permanent deletion requires -PurgeData plus the exact PURGE AXON confirmation and cannot be undone from Axon.",
    ])
    story += [p("Control-panel-only update", "H2x"), p("Extract the Axon Control upgrade ZIP on the Windows host, double-click Update Axon Control.cmd, and approve the administrator prompt. The updater validates its payload, stops only the host-only control task, backs up the installed control panel under %ProgramData%\\Axon\\control-backups, installs the update, verifies it, and rolls back automatically if TCP 8780 does not return.", "Bodyx")]
    GuideDoc(OUTPUT / "Axon_Maintenance_and_Tuning_Guide.pdf", "Axon Maintenance and Tuning Guide").build(story)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    build_setup()
    build_client()
    build_maintenance()
    for path in sorted(OUTPUT.glob("Axon_*_Guide.pdf")):
        print(path)


if __name__ == "__main__":
    main()

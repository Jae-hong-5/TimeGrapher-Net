# -*- coding: utf-8 -*-
"""[보관] 초기 골격 생성기 — 현재 단일 소스는 sequence.drawio다.

⚠ 이 스크립트를 실행하면 Python 모델로 sequence.drawio와 SVG를 다시 만들어
   draw.io에서 손으로 편집한 배치를 덮어쓴다. SVG 갱신은 _drawio_to_svg.py를 쓸 것.

Generate the leveled Run-Lifecycle sequence view (MVVM).

Pages: Level 1 (개요), 2.1 (준비 공통), 2.2 (모드별 시작), 2.3 (측정 루프),
2.4 (사용자/외부 종료), 2.5 (자연 종료), 2.6 (프로그램 종료 teardown).

After the MVC -> MVVM refactor the old single "App layer" lifeline is split into
the three collaborators the refactor separated:

    View              = MainWindow              (Avalonia window, rendering, run-session wiring)
    ViewModel         = MainWindowViewModel      (commands + observable RunState/StatusText)
    RunCommandService = run-command state machine (start/pause/stop orchestration)

Level 1 shows the View/ViewModel/Service split explicitly (the command + binding
path); each Level 2.x detail page carries only the participants that flow actually
touches, so no page is oversized.

The same in-memory model drives two emitters: `sequence.drawio` (editable source)
and one standalone SVG per page (consumed by the Markdown). drawio is not required
to refresh the rendered diagrams. Output is written next to this script.
"""
import os
import textwrap

STEP = 46
TOP = 60
HEAD = 34
FIRST_Y = 150
LW = 175
GAP = 215
BAR_W = 12

C_LIFE = "#1d4ed8"
C_LIFEFILL = "#dae8fc"
C_BAR = "#bfdbfe"
C_CALL = "#1f2937"
C_RET = "#475569"
C_FRAME = "#64748b"
C_REF = "#1d4ed8"
C_DIV = "#94a3b8"
C_TEXT = "#0f172a"
C_NOTE_FILL = "#fef9c3"
C_NOTE_STROKE = "#ca8a04"


def esc(s):
    s = s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")
    return s.replace("\n", "&#10;")


def sesc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def text_w(s, size):
    # Rough advance-width estimate: CJK/wide glyphs ~1em, the rest ~0.55em.
    w = 0.0
    for ch in s:
        w += size if ord(ch) >= 0x1100 else size * 0.55
    return w


class Page:
    def __init__(self, pid, name, participants):
        self.pid = pid
        self.name = name
        self.participants = participants
        self.cx = {p[0]: 40 + GAP * i + LW / 2 for i, p in enumerate(participants)}
        self.lifelines = []
        self.frames = []
        self.bars = []
        self.msgs = []
        self.texts = []
        self.ops = []          # structured records for the SVG emitter
        self.life_ops = []
        self.y = FIRST_Y
        self.act = {p[0]: [] for p in participants}
        self.last_y = FIRST_Y
        self._n = 0
        self._built = False

    def nid(self, tag):
        self._n += 1
        return f"{self.pid}_{tag}{self._n}"

    def page_w(self):
        return 40 + GAP * (len(self.participants) - 1) + LW + 40

    def frame_span(self, li=None, ri=None):
        li = 0 if li is None else li
        ri = len(self.participants) - 1 if ri is None else ri
        return 40 + GAP * li - 8, 40 + GAP * ri + LW + 8

    def msg(self, src, dst, label, kind="call"):
        y = self.y
        a, b = self.cx[src], self.cx[dst]
        sx, tx = (a + 6, b - 6) if b >= a else (a - 6, b + 6)
        if kind == "ret":
            style = (f"html=1;verticalAlign=bottom;startArrow=none;endArrow=open;dashed=1;rounded=0;"
                     f"labelBackgroundColor=#ffffff;strokeColor={C_RET};fontColor=#111827;labelBorderColor=none;")
        else:
            style = (f"html=1;verticalAlign=bottom;startArrow=none;endArrow=block;rounded=0;"
                     f"labelBackgroundColor=#ffffff;strokeColor={C_CALL};fontColor=#111827;labelBorderColor=none;")
        self.msgs.append(
            f'<mxCell id="{self.nid("m")}" value="{esc(label)}" style="{style}" edge="1" parent="1">'
            f'<mxGeometry relative="1" as="geometry"><mxPoint x="{sx}" y="{y}" as="sourcePoint" />'
            f'<mxPoint x="{tx}" y="{y}" as="targetPoint" /></mxGeometry></mxCell>')
        self.ops.append(("msg", a, b, sx, tx, y, label, kind))
        self.last_y = y
        self.y += STEP

    def selfmsg(self, who, label):
        y = self.y
        x0 = self.cx[who] + 6
        x1 = x0 + 49
        style = (f"html=1;verticalAlign=middle;align=left;startArrow=none;endArrow=block;rounded=0;"
                 f"labelBackgroundColor=#ffffff;strokeColor={C_CALL};fontColor=#111827;labelBorderColor=none;")
        self.msgs.append(
            f'<mxCell id="{self.nid("s")}" value="{esc(label)}" style="{style}" edge="1" parent="1">'
            f'<mxGeometry relative="1" as="geometry"><mxPoint x="{x0}" y="{y}" as="sourcePoint" />'
            f'<mxPoint x="{x0}" y="{y + 20}" as="targetPoint" />'
            f'<Array as="points"><mxPoint x="{x1}" y="{y}" /><mxPoint x="{x1}" y="{y + 20}" /></Array>'
            f'<mxPoint x="50" y="0" as="offset" /></mxGeometry></mxCell>')
        self.ops.append(("self", x0, x1, y, label))
        self.last_y = y
        self.y += STEP

    def act_on(self, who):
        self.act[who].append(self.last_y)

    def act_off(self, who):
        if not self.act[who]:
            return
        start = self.act[who].pop()
        h = max(self.last_y - start, 8)
        c = self.cx[who]
        self.bars.append(
            f'<mxCell id="{self.nid("bar")}" value="" style="html=1;points=[];perimeter=orthogonalPerimeter;'
            f'fillColor={C_BAR};strokeColor={C_LIFE};" vertex="1" parent="1">'
            f'<mxGeometry x="{c - BAR_W/2}" y="{start}" width="{BAR_W}" height="{h}" as="geometry" /></mxCell>')
        self.ops.append(("bar", c, start, h))

    def note(self, label, li=None, ri=None):
        left, right = self.frame_span(li, ri)
        y = self.y
        width = right - left - 20
        chars = max(8, int((width - 12) / (text_w("가", 11))))
        lines = textwrap.wrap(label, chars) or [""]
        h = max(34, len(lines) * 15 + 14)
        self.texts.append(
            f'<mxCell id="{self.nid("note")}" value="{esc(label)}" style="shape=note;whiteSpace=wrap;html=1;'
            f'fillColor={C_NOTE_FILL};strokeColor={C_NOTE_STROKE};fontColor={C_TEXT};fontSize=11;align=center;verticalAlign=middle;size=10;" '
            f'vertex="1" parent="1"><mxGeometry x="{left+10}" y="{y}" width="{width}" height="{h}" as="geometry" /></mxCell>')
        self.ops.append(("note", label, left + 10, y, width, h))
        self.y += h + 16

    def ref(self, caption, li=None, ri=None):
        left, right = self.frame_span(li, ri)
        top = self.y
        bottom = top + 50
        self.frames.append(
            f'<mxCell id="{self.nid("ref")}" value="ref" style="shape=umlFrame;whiteSpace=wrap;html=1;fillColor=none;'
            f'strokeColor={C_REF};fontColor={C_TEXT};fontSize=12;fontStyle=1;width=46;height=22;verticalAlign=top;align=left;" '
            f'vertex="1" parent="1"><mxGeometry x="{left}" y="{top}" width="{right-left}" height="50" as="geometry" /></mxCell>')
        self.texts.append(
            f'<mxCell id="{self.nid("refc")}" value="{esc(caption)}" style="text;html=1;strokeColor=none;fillColor=none;'
            f'align=center;verticalAlign=middle;fontSize=12;fontColor={C_TEXT};" vertex="1" parent="1">'
            f'<mxGeometry x="{left+10}" y="{top+16}" width="{right-left-20}" height="32" as="geometry" /></mxCell>')
        self.ops.append(("ref", caption, left, top, right, bottom))
        self.y = bottom + 14

    def frag(self, kind, branches, li=None, ri=None):
        left, right = self.frame_span(li, ri)
        top = self.y
        self.y += 30
        for i, (guard, fn) in enumerate(branches):
            if i > 0:
                dy = self.y
                self.frames.append(
                    f'<mxCell id="{self.nid("div")}" value="" style="html=1;endArrow=none;startArrow=none;dashed=1;'
                    f'strokeColor={C_DIV};" edge="1" parent="1"><mxGeometry relative="1" as="geometry">'
                    f'<mxPoint x="{left}" y="{dy}" as="sourcePoint" /><mxPoint x="{right}" y="{dy}" as="targetPoint" /></mxGeometry></mxCell>')
                self.ops.append(("div", left, right, dy))
                self.y += 6
            if guard:
                gy = self.y
                self.texts.append(
                    f'<mxCell id="{self.nid("g")}" value="{esc("[" + guard + "]")}" style="text;html=1;strokeColor=none;'
                    f'fillColor=none;align=left;verticalAlign=middle;fontSize=11;fontColor={C_TEXT};fontStyle=2;" vertex="1" parent="1">'
                    f'<mxGeometry x="{left+24}" y="{gy}" width="{right-left-40}" height="20" as="geometry" /></mxCell>')
                self.ops.append(("guard", "[" + guard + "]", left + 24, gy))
                self.y += 40
            fn(self)
            self.y += 6
        bottom = self.y + 6
        self.frames.append(
            f'<mxCell id="{self.nid("frag")}" value="{kind}" style="shape=umlFrame;whiteSpace=wrap;html=1;fillColor=none;'
            f'strokeColor={C_FRAME};fontColor={C_TEXT};fontSize=12;fontStyle=1;width=54;height=24;verticalAlign=top;align=left;" '
            f'vertex="1" parent="1"><mxGeometry x="{left}" y="{top}" width="{right-left}" height="{bottom-top}" as="geometry" /></mxCell>')
        self.ops.append(("frame", kind, left, top, right, bottom))
        self.y = bottom + 12

    def build(self):
        if self._built:
            return
        self._built = True
        lh = self.y - TOP + 20
        for i, (key, label, is_actor) in enumerate(self.participants):
            if is_actor:
                w = 60
                x = self.cx[key] - w / 2
            else:
                w = LW
                x = 40 + GAP * i
            extra = "participant=umlActor;" if is_actor else ""
            fill = "none" if is_actor else C_LIFEFILL
            self.lifelines.append(
                f'<mxCell id="{self.pid}_{key}" value="{esc(label)}" style="shape=umlLifeline;{extra}'
                f'perimeter=lifelinePerimeter;whiteSpace=wrap;html=1;container=1;collapsible=0;recursiveResize=0;'
                f'outlineConnect=0;size=48;fillColor={fill};strokeColor={C_LIFE};fontColor={C_TEXT};fontSize=12;" '
                f'vertex="1" parent="1"><mxGeometry x="{x}" y="{TOP}" width="{w}" height="{lh}" as="geometry" /></mxCell>')
            self.life_ops.append((key, label, is_actor, x, w, lh, self.cx[key]))

    # ---- drawio emitter ----
    def xml(self):
        self.build()
        pw = self.page_w()
        ph = self.y + 40
        body = "".join(self.lifelines + self.frames + self.bars + self.msgs + self.texts)
        return (f'<diagram id="{self.pid}" name="{esc(self.name)}">'
                f'<mxGraphModel grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" '
                f'page="1" pageScale="1" pageWidth="{int(pw)}" pageHeight="{int(ph)}" math="0" shadow="0">'
                f'<root><mxCell id="0" /><mxCell id="1" parent="0" />{body}</root></mxGraphModel></diagram>')

    # ---- SVG emitter ----
    def _bounds(self):
        # Widen the canvas so message / self-message labels never clip when a
        # lifeline sits near the page edge.
        left, right = 40.0, float(self.page_w() - 40)
        for op in self.ops:
            if op[0] == "msg":
                sx, tx, label = op[3], op[4], op[6].replace("\n", " ")
                mid = (sx + tx) / 2
                w = text_w(label, 11) + 6
                left = min(left, mid - w / 2)
                right = max(right, mid + w / 2)
            elif op[0] == "self":
                x1, label = op[2], op[4].replace("\n", " ")
                right = max(right, x1 + 9 + text_w(label, 11) + 6)
            elif op[0] == "note":
                right = max(right, op[2] + op[4])
        return left - 16, right + 16

    def svg(self):
        self.build()
        min_x, max_x = self._bounds()
        vx = int(min(0, min_x))
        pw = int(max_x - vx)
        ph = int(self.y + 40)
        out = []
        # 1) lifelines (heads + dashed lines)
        for key, label, is_actor, x, w, lh, cx in self.life_ops:
            bottom = TOP + lh
            if is_actor:
                out.append(self._svg_actor(cx, label, bottom))
            else:
                out.append(self._svg_object(cx, x, w, label, bottom))
        # 2) frames (alt/opt/loop/ref) + dividers, behind bars/messages
        for op in self.ops:
            if op[0] == "frame":
                out.append(self._svg_frame(op[1], op[2], op[3], op[4], op[5], False))
            elif op[0] == "ref":
                out.append(self._svg_frame("ref", op[2], op[3], op[4], op[5], True))
                out.append(self._svg_refcaption(op[1], op[2], op[4], op[3]))
            elif op[0] == "div":
                out.append(f'<line x1="{op[1]}" y1="{op[3]}" x2="{op[2]}" y2="{op[3]}" '
                           f'stroke="{C_DIV}" stroke-width="1" stroke-dasharray="4 4"/>')
        # 3) activation bars
        for op in self.ops:
            if op[0] == "bar":
                out.append(f'<rect x="{op[1]-BAR_W/2:.1f}" y="{op[2]}" width="{BAR_W}" height="{op[3]}" '
                           f'fill="{C_BAR}" stroke="{C_LIFE}" stroke-width="1"/>')
        # 4) messages + their labels
        for op in self.ops:
            if op[0] == "msg":
                out.append(self._svg_msg(op[1], op[2], op[3], op[4], op[5], op[6], op[7]))
            elif op[0] == "self":
                out.append(self._svg_self(op[1], op[2], op[3], op[4]))
        # 5) top texts (guards, notes)
        for op in self.ops:
            if op[0] == "guard":
                out.append(f'<text x="{op[2]}" y="{op[3]+14}" font-family="Helvetica,Arial,sans-serif" '
                           f'font-size="11" font-style="italic" fill="{C_TEXT}">{sesc(op[1])}</text>')
            elif op[0] == "note":
                out.append(self._svg_note(op[1], op[2], op[3], op[4], op[5]))
        inner = "".join(out)
        return (f'<?xml version="1.0" encoding="UTF-8"?>\n'
                f'<svg xmlns="http://www.w3.org/2000/svg" width="{pw}" height="{ph}" '
                f'viewBox="{vx} 0 {pw} {ph}" font-family="Helvetica,Arial,sans-serif">'
                f'<rect x="{vx}" y="0" width="{pw}" height="{ph}" fill="#ffffff"/>{inner}</svg>')

    @staticmethod
    def _svg_object(cx, x, w, label, bottom):
        lines = label.split("\n")
        n = len(lines)
        ty0 = TOP + HEAD / 2 - (n - 1) * 7 + 4
        txt = "".join(
            f'<text x="{cx}" y="{ty0 + i*14:.0f}" text-anchor="middle" font-size="12" fill="{C_TEXT}">{sesc(t)}</text>'
            for i, t in enumerate(lines))
        return (f'<rect x="{x}" y="{TOP}" width="{w}" height="{HEAD}" fill="{C_LIFEFILL}" stroke="{C_LIFE}"/>'
                f'<line x1="{cx}" y1="{TOP+HEAD}" x2="{cx}" y2="{bottom}" stroke="{C_LIFE}" '
                f'stroke-width="1" stroke-dasharray="3 3"/>{txt}')

    @staticmethod
    def _svg_actor(cx, label, bottom):
        lines = label.split("\n")
        txt = "".join(
            f'<text x="{cx}" y="{TOP+50 + i*12}" text-anchor="middle" font-size="12" fill="{C_TEXT}">{sesc(t)}</text>'
            for i, t in enumerate(lines))
        return (f'<g stroke="{C_LIFE}" stroke-width="1.3" fill="none">'
                f'<circle cx="{cx}" cy="{TOP+8}" r="6"/>'
                f'<line x1="{cx}" y1="{TOP+14}" x2="{cx}" y2="{TOP+27}"/>'
                f'<line x1="{cx-11}" y1="{TOP+19}" x2="{cx+11}" y2="{TOP+19}"/>'
                f'<line x1="{cx}" y1="{TOP+27}" x2="{cx-9}" y2="{TOP+39}"/>'
                f'<line x1="{cx}" y1="{TOP+27}" x2="{cx+9}" y2="{TOP+39}"/></g>'
                f'<line x1="{cx}" y1="{TOP+44}" x2="{cx}" y2="{bottom}" stroke="{C_LIFE}" '
                f'stroke-width="1" stroke-dasharray="3 3"/>{txt}')

    @staticmethod
    def _arrow(x, y, dirn, color, filled):
        if dirn >= 0:
            pts = f"{x},{y} {x-9},{y-4} {x-9},{y+4}"
            d = f"M {x-9} {y-4} L {x} {y} L {x-9} {y+4}"
        else:
            pts = f"{x},{y} {x+9},{y-4} {x+9},{y+4}"
            d = f"M {x+9} {y-4} L {x} {y} L {x+9} {y+4}"
        if filled:
            return f'<polygon points="{pts}" fill="{color}"/>'
        return f'<path d="{d}" fill="none" stroke="{color}" stroke-width="1.3"/>'

    @classmethod
    def _svg_msg(cls, a, b, sx, tx, y, label, kind):
        label = label.replace("\n", " ")
        dirn = 1 if tx >= sx else -1
        if kind == "ret":
            line = (f'<line x1="{sx}" y1="{y}" x2="{tx}" y2="{y}" stroke="{C_RET}" '
                    f'stroke-width="1.2" stroke-dasharray="5 4"/>')
            head = cls._arrow(tx, y, dirn, C_RET, False)
        else:
            line = f'<line x1="{sx}" y1="{y}" x2="{tx}" y2="{y}" stroke="{C_CALL}" stroke-width="1.4"/>'
            head = cls._arrow(tx, y, dirn, C_CALL, True)
        mid = (sx + tx) / 2
        w = text_w(label, 11) + 6
        bg = (f'<rect x="{mid-w/2:.1f}" y="{y-15}" width="{w:.1f}" height="13" fill="#ffffff"/>')
        txt = (f'<text x="{mid:.1f}" y="{y-5}" text-anchor="middle" font-size="11" '
               f'fill="#111827">{sesc(label)}</text>')
        return line + head + bg + txt

    @staticmethod
    def _svg_self(x0, x1, y, label):
        label = label.replace("\n", " ")
        path = (f'<polyline points="{x0},{y} {x1},{y} {x1},{y+20} {x0},{y+20}" '
                f'fill="none" stroke="{C_CALL}" stroke-width="1.4"/>')
        head = f'<polygon points="{x0},{y+20} {x0+9},{y+16} {x0+9},{y+24}" fill="{C_CALL}"/>'
        w = text_w(label, 11) + 6
        bg = f'<rect x="{x1+6}" y="{y+3}" width="{w:.1f}" height="15" fill="#ffffff"/>'
        txt = (f'<text x="{x1+9}" y="{y+15}" font-size="11" fill="#111827">{sesc(label)}</text>')
        return path + head + bg + txt

    @staticmethod
    def _svg_frame(kind, left, top, right, bottom, is_ref):
        color = C_REF if is_ref else C_FRAME
        tabw = text_w(kind, 12) + 16
        rect = (f'<rect x="{left}" y="{top}" width="{right-left}" height="{bottom-top}" '
                f'fill="none" stroke="{color}" stroke-width="1.2"/>')
        tab = (f'<path d="M {left} {top} L {left} {top+18} L {left+tabw-8:.0f} {top+18} '
               f'L {left+tabw:.0f} {top+10} L {left+tabw:.0f} {top} Z" '
               f'fill="#eef2f7" stroke="{color}" stroke-width="1.2"/>')
        txt = (f'<text x="{left+8}" y="{top+13}" font-size="12" font-weight="bold" '
               f'fill="{C_TEXT}">{sesc(kind)}</text>')
        return rect + tab + txt

    @staticmethod
    def _svg_refcaption(caption, left, right, top):
        cx = (left + right) / 2
        return (f'<text x="{cx:.0f}" y="{top+34}" text-anchor="middle" font-size="12" '
                f'fill="{C_TEXT}">{sesc(caption)}</text>')

    @staticmethod
    def _svg_note(label, x, y, w, h):
        fold = 10
        box = (f'<path d="M {x} {y} L {x+w-fold} {y} L {x+w} {y+fold} L {x+w} {y+h} '
               f'L {x} {y+h} Z" fill="{C_NOTE_FILL}" stroke="{C_NOTE_STROKE}" stroke-width="1"/>'
               f'<path d="M {x+w-fold} {y} L {x+w-fold} {y+fold} L {x+w} {y+fold}" '
               f'fill="none" stroke="{C_NOTE_STROKE}" stroke-width="1"/>')
        chars = max(8, int((w - 12) / (text_w("가", 11))))
        lines = textwrap.wrap(label, chars) or [""]
        ty0 = y + (h - (len(lines) - 1) * 15) / 2 + 4
        txt = "".join(
            f'<text x="{x+w/2:.0f}" y="{ty0 + i*15:.0f}" text-anchor="middle" font-size="11" '
            f'fill="{C_TEXT}">{sesc(t)}</text>' for i, t in enumerate(lines))
        return box + txt


# ---- Level 1 termination triggers (folded into the overview, simplified) ----
# The three stop triggers converge on the same View-driven worker teardown. The
# user/external paths go through RunCommandService; Playback's natural end is
# handled by the View's worker callback directly (Svc bypassed).
def user_stop(pg):
    pg.msg("User", "VM", "Reset 클릭 (ResetCommand)")
    pg.msg("VM", "Svc", "Reset()", "ret")
    pg.msg("Svc", "View", "StopMode(CurrentMode)")


def ext_stop(pg):
    pg.msg("Input", "View", "CaptureEnded(token)", "ret")
    pg.msg("View", "Svc", "StopRunAndRefreshDevices()")
    pg.msg("Svc", "View", "StopMode(CurrentMode)")


def natural_stop(pg):
    pg.msg("Input", "View", "DoneReadingFile (WAV EOF) — View 직접 처리 (Svc 우회)", "ret")


pages = []

# Level 1 · 실행 수명주기 개요 (통합) — 입력 시작·종료·프로그램 종료를 간소화해 인라인하고,
# 측정 루프만 Level 2로 ref한다. 실행 제어 상태 전이는 상태 머신 뷰에서 다룬다.
p = Page("level1", "Level 1 · 실행 수명주기 개요",
         [("User", "User", True),
          ("View", "View\n(MainWindow)", False),
          ("VM", "ViewModel\n(MainWindowViewModel)", False),
          ("Svc", "RunCommandService", False),
          ("Sess", "RunSessionController", False),
          ("Input", "Input worker", False),
          ("Analysis", "AnalysisWorker", False)])

# 실행
p.msg("User", "View", "프로그램 실행")
p.act_on("View")
p.selfmsg("View", "ViewModel 생성·DataContext 바인딩,\n서비스 그래프 구성, 입력 장치 목록 로드")
p.act_off("View")

# 시작 (입력 준비 + 모드별 시작을 간소화 인라인)
p.msg("User", "View", "Start 클릭")
p.msg("View", "VM", "PlayPauseCommand 실행 (Command 바인딩)")
p.act_on("VM")
p.msg("VM", "Svc", "StartAsync()")
p.act_on("Svc")
p.msg("Svc", "View", "StartMode(CurrentMode)")
p.act_on("View")
p.msg("View", "Sess", "PrepareInputRun(sampleRate)")
p.act_on("Sess")
p.selfmsg("Sess", "MasterAudioBuffer·AnalysisWorker 생성")
p.msg("Sess", "Analysis", "Start()")
p.msg("Sess", "View", "buffer + runSessionToken", "ret")
p.act_off("Sess")
p.msg("View", "Input", "create worker / Start()  (Live / Playback / Simulation)")
p.act_off("View"); p.act_off("Svc"); p.act_off("VM")

# 측정 (유일한 세분화 페이지)
p.ref("Level 2 · 측정 중 분석 반복 흐름", 1, 6)

# 종료 (사용자 / 외부 / Playback 자연 종료 트리거를 간소화 인라인)
p.frag("alt", [
    ("사용자 종료 — Pause 후 Reset", user_stop),
    ("외부 비정상 종료 — Live capture 끊김", ext_stop),
    ("Playback 자연 종료 — WAV EOF", natural_stop),
])
p.last_y = p.y  # converge below the trigger alt
p.act_on("View")
p.msg("View", "Sess", "StopInputWorker() / StopAnalysisThread()")
p.act_on("Sess")
p.msg("Sess", "Input", "TryStop(timeout)")
p.msg("Sess", "Analysis", "TryStop / CompleteInput(timeout)")
p.act_off("Sess")
p.selfmsg("View", "녹음 close 확인 · 세션 무효화 · (Playback/Sim 오디오 복원)")
p.act_off("View")

# 프로그램 종료
p.msg("User", "View", "프로그램 종료 (창 닫기)")
p.act_on("View")
p.selfmsg("View", "OnWindowClosed(): 세션 무효화 · 입력·분석 worker 정지")
p.msg("View", "User", "프로세스 종료", "ret")
p.act_off("View")
pages.append(p)

# Level 2 · 측정 중 분석 반복 흐름 (유일한 세분화 페이지)
p = Page("level2", "Level 2 · 측정 중 분석 반복 흐름",
         [("Sess", "RunSessionController", False), ("Input", "Input worker", False),
          ("Buffer", "MasterAudioBuffer", False), ("Analysis", "AnalysisWorker", False),
          ("Core", "Core pipeline", False), ("View", "View\n(MainWindow)", False),
          ("VM", "ViewModel", False)])

def loopbody(pg):
    # The Input worker drives each iteration; show its produce-phase activation.
    pg.last_y = pg.y
    pg.act_on("Input")
    pg.msg("Input", "Buffer", "WriteSamples(block)")
    pg.act_on("Buffer"); pg.msg("Buffer", "Input", "samples stored", "ret"); pg.act_off("Buffer")
    pg.msg("Input", "Sess", "DataReady", "ret")
    pg.act_off("Input")
    pg.act_on("Sess")
    pg.selfmsg("Sess", "runSessionToken 확인")
    pg.msg("Sess", "Analysis", "NotifyDataReady()")
    pg.act_on("Analysis"); pg.msg("Analysis", "Sess", "wakeup signaled", "ret"); pg.act_off("Sess")
    pg.msg("Analysis", "Buffer", "CopyAnalysisSamples()")
    pg.act_on("Buffer"); pg.msg("Buffer", "Analysis", "analysis block", "ret"); pg.act_off("Buffer")
    pg.msg("Analysis", "Core", "Process(block)")
    pg.act_on("Core"); pg.msg("Core", "Analysis", "detection / metrics / projected frame data", "ret"); pg.act_off("Core")
    pg.msg("Analysis", "View", "AnalysisFrameReady(frame)", "ret")
    pg.act_on("View")
    pg.selfmsg("View", "HandleAnalysisFrame(): 그래프 렌더")
    pg.msg("View", "VM", "Present(): Status·AwaitingBeatSync·Review 갱신")
    pg.act_on("VM"); pg.msg("VM", "View", "ok", "ret"); pg.act_off("VM")
    pg.msg("View", "Analysis", "frame accepted", "ret")
    pg.act_off("View"); pg.act_off("Analysis")

p.frag("loop", [("측정 중: 입력 block마다 — Live·Simulation은 정지까지 무한 반복, Playback은 WAV EOF까지", loopbody)])
pages.append(p)

here = os.path.dirname(os.path.abspath(__file__))

drawio = ('<mxfile host="app.diagrams.net" agent="Claude" type="device">'
          + "".join(pg.xml() for pg in pages) + "</mxfile>")
with open(os.path.join(here, "sequence.drawio"), "w", encoding="utf-8") as f:
    f.write(drawio)

assets = os.path.join(here, "assets", "uml25")
os.makedirs(assets, exist_ok=True)
valid = {f"run-lifecycle-seq-{pg.pid}.svg" for pg in pages}
for pg in pages:
    with open(os.path.join(assets, f"run-lifecycle-seq-{pg.pid}.svg"), "w", encoding="utf-8") as f:
        f.write(pg.svg())
# Drop SVGs from the previous (6-page) layout so the assets dir stays in sync.
for fn in os.listdir(assets):
    if fn.startswith("run-lifecycle-seq-") and fn.endswith(".svg") and fn not in valid:
        os.remove(os.path.join(assets, fn))

print("wrote sequence.drawio and", len(pages), "SVGs")

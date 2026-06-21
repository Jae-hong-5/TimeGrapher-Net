# -*- coding: utf-8 -*-
"""sequence.drawio → 페이지별 SVG 변환기 (drawio가 단일 소스).

drawio 데스크톱/CLI 없이 `sequence.drawio`를 읽어 각 diagram을
`assets/uml25/run-lifecycle-seq-<pid>.svg`로 렌더한다. drawio에서 손으로 조정한
좌표(생명선 길이·화살표 위치 등)를 그대로 사용한다.

주의: SVG 스타일(폰트·간격·생명선 머리 높이)은 이 렌더러 기준이라 draw.io 앱의
SVG export와 픽셀 단위로 같지는 않다. 배치(좌표)는 drawio를 따른다.

사용법:  python _drawio_to_svg.py
"""
import os
import xml.etree.ElementTree as ET
import textwrap

TOP = 60          # drawio 생명선 머리 y (sequence.drawio 전 페이지 공통)
HEAD = 34         # 렌더러가 그리는 머리 박스 높이 (drawio size=48과 무관한 표시 스타일)
BAR_W = 12

C_LIFE = "#1d4ed8"
C_LIFEFILL = "#dae8fc"
C_BAR = "#bfdbfe"
C_CALL = "#1f2937"
C_RET = "#475569"
C_FRAME = "#64748b"
C_REF = "#1d4ed8"
C_TEXT = "#0f172a"
C_NOTE_FILL = "#fef9c3"
C_NOTE_STROKE = "#ca8a04"

FRAME_KINDS = ("alt", "opt", "loop", "ref", "par", "break", "neg", "critical")


def sesc(s):
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def text_w(s, size):
    w = 0.0
    for ch in s:
        w += size if ord(ch) >= 0x1100 else size * 0.55
    return w


# ---- SVG 프리미티브 (좌표는 drawio에서 읽은 값을 그대로 사용) ----
def svg_object(cx, x, w, label, bottom):
    lines = label.split("\n")
    n = len(lines)
    ty0 = TOP + HEAD / 2 - (n - 1) * 7 + 4
    txt = "".join(
        f'<text x="{cx}" y="{ty0 + i*14:.0f}" text-anchor="middle" font-size="12" fill="{C_TEXT}">{sesc(t)}</text>'
        for i, t in enumerate(lines))
    return (f'<rect x="{x}" y="{TOP}" width="{w}" height="{HEAD}" fill="{C_LIFEFILL}" stroke="{C_LIFE}"/>'
            f'<line x1="{cx}" y1="{TOP+HEAD}" x2="{cx}" y2="{bottom}" stroke="{C_LIFE}" '
            f'stroke-width="1" stroke-dasharray="3 3"/>{txt}')


def svg_actor(cx, label, bottom):
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


def arrow(x, y, dirn, color, filled):
    if dirn >= 0:
        pts = f"{x},{y} {x-9},{y-4} {x-9},{y+4}"
        d = f"M {x-9} {y-4} L {x} {y} L {x-9} {y+4}"
    else:
        pts = f"{x},{y} {x+9},{y-4} {x+9},{y+4}"
        d = f"M {x+9} {y-4} L {x} {y} L {x+9} {y+4}"
    if filled:
        return f'<polygon points="{pts}" fill="{color}"/>'
    return f'<path d="{d}" fill="none" stroke="{color}" stroke-width="1.3"/>'


def svg_msg(sx, tx, y, label, kind):
    label = label.replace("\n", " ")
    dirn = 1 if tx >= sx else -1
    if kind == "ret":
        line = (f'<line x1="{sx}" y1="{y}" x2="{tx}" y2="{y}" stroke="{C_RET}" '
                f'stroke-width="1.2" stroke-dasharray="5 4"/>')
        head = arrow(tx, y, dirn, C_RET, False)
    else:
        line = f'<line x1="{sx}" y1="{y}" x2="{tx}" y2="{y}" stroke="{C_CALL}" stroke-width="1.4"/>'
        head = arrow(tx, y, dirn, C_CALL, True)
    mid = (sx + tx) / 2
    w = text_w(label, 11) + 6
    bg = f'<rect x="{mid-w/2:.1f}" y="{y-15}" width="{w:.1f}" height="13" fill="#ffffff"/>'
    txt = f'<text x="{mid:.1f}" y="{y-5}" text-anchor="middle" font-size="11" fill="#111827">{sesc(label)}</text>'
    return line + head + bg + txt


def svg_self(x0, x1, y1, y2, label):
    label = label.replace("\n", " ")
    path = (f'<polyline points="{x0},{y1} {x1},{y1} {x1},{y2} {x0},{y2}" '
            f'fill="none" stroke="{C_CALL}" stroke-width="1.4"/>')
    head = f'<polygon points="{x0},{y2} {x0+9},{y2-4} {x0+9},{y2+4}" fill="{C_CALL}"/>'
    w = text_w(label, 11) + 6
    ty = (y1 + y2) / 2
    bg = f'<rect x="{x1+6}" y="{ty-7:.1f}" width="{w:.1f}" height="15" fill="#ffffff"/>'
    txt = f'<text x="{x1+9}" y="{ty+4:.1f}" font-size="11" fill="#111827">{sesc(label)}</text>'
    return path + head + bg + txt


def svg_frame(kind, left, top, right, bottom):
    is_ref = kind == "ref"
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


def svg_divider(left, right, y):
    return (f'<line x1="{left}" y1="{y}" x2="{right}" y2="{y}" '
            f'stroke="{C_FRAME}" stroke-width="1" stroke-dasharray="4 4"/>')


def svg_text(value, x, y, w, h, italic, center, size):
    bx = x + w / 2 if center else x
    anchor = "middle" if center else "start"
    style = 'font-style="italic" ' if italic else ""
    baseline = y + h / 2 + 4
    return (f'<text x="{bx:.0f}" y="{baseline:.0f}" text-anchor="{anchor}" '
            f'font-size="{size}" {style}fill="{C_TEXT}">{sesc(value)}</text>')


def svg_bar(x, y, w, h):
    return (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" '
            f'fill="{C_BAR}" stroke="{C_LIFE}" stroke-width="1"/>')


def svg_note(label, x, y, w, h):
    fold = 10
    box = (f'<path d="M {x} {y} L {x+w-fold} {y} L {x+w} {y+fold} L {x+w} {y+h} '
           f'L {x} {y+h} Z" fill="{C_NOTE_FILL}" stroke="{C_NOTE_STROKE}" stroke-width="1"/>'
           f'<path d="M {x+w-fold} {y} L {x+w-fold} {y+fold} L {x+w} {y+fold}" '
           f'fill="none" stroke="{C_NOTE_STROKE}" stroke-width="1"/>')
    chars = max(8, int((w - 12) / text_w("가", 11)))
    lines = textwrap.wrap(label, chars) or [""]
    ty0 = y + (h - (len(lines) - 1) * 15) / 2 + 4
    txt = "".join(
        f'<text x="{x+w/2:.0f}" y="{ty0 + i*15:.0f}" text-anchor="middle" font-size="11" '
        f'fill="{C_TEXT}">{sesc(t)}</text>' for i, t in enumerate(lines))
    return box + txt


# ---- drawio 파싱 ----
def _fnum(s, default=0.0):
    try:
        return float(s)
    except (TypeError, ValueError):
        return default


def _edge_points(geo):
    pts = {}
    for mp in geo.findall("mxPoint"):
        role = mp.get("as")
        if role:
            pts[role] = (_fnum(mp.get("x")), _fnum(mp.get("y")))
    arr = geo.find("Array")
    apoints = []
    if arr is not None:
        for mp in arr.findall("mxPoint"):
            apoints.append((_fnum(mp.get("x")), _fnum(mp.get("y"))))
    return pts, apoints


def render_diagram(diagram):
    model = diagram.find("mxGraphModel")
    pw = int(_fnum(model.get("pageWidth"), 800))
    ph = int(_fnum(model.get("pageHeight"), 600))
    cells = model.find("root").findall("mxCell")

    lifelines, frames, dividers, bars, msgs, selfs, texts, notes = [], [], [], [], [], [], [], []
    pid = "page"

    for c in cells:
        style = c.get("style") or ""
        value = c.get("value") or ""
        geo = c.find("mxGeometry")
        if geo is None:
            continue

        if "shape=umlLifeline" in style:
            cid = c.get("id") or ""
            if "_" in cid:
                pid = cid.rsplit("_", 1)[0]
            x, w, h = _fnum(geo.get("x")), _fnum(geo.get("width")), _fnum(geo.get("height"))
            cx = x + w / 2
            lifelines.append(("actor" if "participant=umlActor" in style else "object",
                              cx, x, w, value, TOP + h))
        elif "shape=umlFrame" in style:
            x, y, w, h = _fnum(geo.get("x")), _fnum(geo.get("y")), _fnum(geo.get("width")), _fnum(geo.get("height"))
            kind = value if value in FRAME_KINDS else "alt"
            frames.append((kind, x, y, x + w, y + h))
        elif "shape=note" in style:
            notes.append((value, _fnum(geo.get("x")), _fnum(geo.get("y")),
                          _fnum(geo.get("width")), _fnum(geo.get("height"))))
        elif c.get("edge") == "1":
            pts, apoints = _edge_points(geo)
            sp = pts.get("sourcePoint")
            tp = pts.get("targetPoint")
            if sp is None or tp is None:
                continue
            if "strokeColor=#94a3b8" in style:        # 분기 divider
                dividers.append((sp[0], tp[0], sp[1]))
            elif apoints:                              # self-message (loop)
                selfs.append((sp[0], apoints[0][0], sp[1], tp[1], value))
            else:                                      # 직선 호출/응답
                kind = "ret" if "endArrow=open" in style else "call"
                msgs.append((sp[0], tp[0], sp[1], value, kind))
        elif "fillColor=#bfdbfe" in style:            # 활성 막대
            bars.append((_fnum(geo.get("x")), _fnum(geo.get("y")),
                         _fnum(geo.get("width")), _fnum(geo.get("height"))))
        elif style.startswith("text;"):               # guard / ref caption
            size = 12 if "fontSize=12" in style else 11
            texts.append((value, _fnum(geo.get("x")), _fnum(geo.get("y")),
                          _fnum(geo.get("width")), _fnum(geo.get("height")),
                          "fontStyle=2" in style, "align=center" in style, size))

    # 라벨이 페이지를 넘으면 캔버스 확장
    min_x, max_x = 0.0, float(pw)
    for sx, tx, y, label, kind in msgs:
        mid = (sx + tx) / 2
        w = text_w(label.replace("\n", " "), 11) + 6
        min_x = min(min_x, mid - w / 2)
        max_x = max(max_x, mid + w / 2)
    for x0, x1, y1, y2, label in selfs:
        max_x = max(max_x, x1 + 9 + text_w(label.replace("\n", " "), 11) + 6)
    for _, x, y, w, h in notes:
        max_x = max(max_x, x + w)
    vx = int(min(0, min_x) - (16 if min_x < 0 else 0))
    width = int(max(max_x, pw) - vx + (16 if max_x > pw else 0))

    out = []
    for kind, cx, x, w, label, bottom in lifelines:
        out.append(svg_actor(cx, label, bottom) if kind == "actor"
                   else svg_object(cx, x, w, label, bottom))
    for kind, left, top, right, bottom in frames:
        out.append(svg_frame(kind, left, top, right, bottom))
    for left, right, y in dividers:
        out.append(svg_divider(left, right, y))
    for x, y, w, h in bars:
        out.append(svg_bar(x, y, w, h))
    for sx, tx, y, label, kind in msgs:
        out.append(svg_msg(sx, tx, y, label, kind))
    for x0, x1, y1, y2, label in selfs:
        out.append(svg_self(x0, x1, y1, y2, label))
    for value, x, y, w, h, italic, center, size in texts:
        out.append(svg_text(value, x, y, w, h, italic, center, size))
    for label, x, y, w, h in notes:
        out.append(svg_note(label, x, y, w, h))

    svg = (f'<?xml version="1.0" encoding="UTF-8"?>\n'
           f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{ph}" '
           f'viewBox="{vx} 0 {width} {ph}" font-family="Helvetica,Arial,sans-serif">'
           f'<rect x="{vx}" y="0" width="{width}" height="{ph}" fill="#ffffff"/>'
           f'{"".join(out)}</svg>')
    return pid, svg


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    src = os.path.join(here, "sequence.drawio")
    assets = os.path.join(here, "assets", "uml25")
    os.makedirs(assets, exist_ok=True)
    root = ET.parse(src).getroot()
    n = 0
    for diagram in root.findall("diagram"):
        pid, svg = render_diagram(diagram)
        with open(os.path.join(assets, f"run-lifecycle-seq-{pid}.svg"), "w", encoding="utf-8") as f:
            f.write(svg)
        n += 1
    print(f"rendered {n} SVGs from sequence.drawio")


if __name__ == "__main__":
    main()

# -*- coding: utf-8 -*-
"""Generate state.drawio: the run-lifecycle control state machine.

States mirror RunUiState / the State Pattern in RunCommandService.States.cs:
Stopped, Starting, Running, Paused, Stopping, StopFailed.
Output is written next to this script.
"""
import os

C_FILL = "#dae8fc"
C_STROKE = "#1d4ed8"
C_EDGE = "#1f2937"

cells = []
nid = [0]


def cid(tag):
    nid[0] += 1
    return f"s_{tag}{nid[0]}"


def esc(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
             .replace('"', "&quot;").replace("\n", "&#10;"))


def state(key, label, x, y, w=140, h=52):
    cells.append(
        f'<mxCell id="{key}" value="{esc(label)}" style="rounded=1;arcSize=24;whiteSpace=wrap;html=1;'
        f'fillColor={C_FILL};strokeColor={C_STROKE};fontColor=#0f172a;fontSize=13;fontStyle=1;" '
        f'vertex="1" parent="1"><mxGeometry x="{x}" y="{y}" width="{w}" height="{h}" as="geometry" /></mxCell>')


def initial(key, x, y):
    cells.append(
        f'<mxCell id="{key}" value="" style="ellipse;fillColor=#0f172a;strokeColor=#0f172a;" '
        f'vertex="1" parent="1"><mxGeometry x="{x}" y="{y}" width="16" height="16" as="geometry" /></mxCell>')


def edge(src, dst, label, exit=None, entry=None, points=None):
    s = ("edgeStyle=orthogonalEdgeStyle;rounded=1;html=1;endArrow=block;"
         f"strokeColor={C_EDGE};fontColor=#111827;labelBackgroundColor=#ffffff;fontSize=12;")
    if exit:
        s += f"exitX={exit[0]};exitY={exit[1]};exitDx=0;exitDy=0;"
    if entry:
        s += f"entryX={entry[0]};entryY={entry[1]};entryDx=0;entryDy=0;"
    geo = '<mxGeometry relative="1" as="geometry">'
    if points:
        geo += '<Array as="points">' + ''.join(f'<mxPoint x="{px}" y="{py}" />' for px, py in points) + '</Array>'
    geo += '</mxGeometry>'
    cells.append(
        f'<mxCell id="{cid("e")}" value="{esc(label)}" style="{s}" edge="1" parent="1" '
        f'source="{src}" target="{dst}">{geo}</mxCell>')


# --- states (left->right lifecycle; stop paths drop down) ---
initial("init", 44, 78)
state("Stopped", "Stopped", 92, 60)
state("Starting", "Starting", 312, 60)
state("Running", "Running", 532, 60)
state("Paused", "Paused", 752, 60)
state("Stopping", "Stopping", 532, 270)
state("StopFailed", "StopFailed", 792, 270, w=150)

# --- transitions ---
edge("init", "Stopped", "")
edge("Stopped", "Starting", "Start", exit=(1, 0.5), entry=(0, 0.5))
edge("Starting", "Running", "시작 성공", exit=(1, 0.5), entry=(0, 0.5))
edge("Starting", "Stopped", "시작 실패 / 정리",
     exit=(0.5, 0), entry=(0.5, 0), points=[(382, 30), (162, 30)])
edge("Running", "Paused", "Pause", exit=(1, 0.3), entry=(0, 0.3))
edge("Paused", "Running", "Resume", exit=(0, 0.7), entry=(1, 0.7))
edge("Running", "Stopping", "Stop / Reset", exit=(0.5, 1), entry=(0.5, 0))
edge("Paused", "Stopping", "Stop / Reset", exit=(0.5, 1), entry=(0.9, 0),
     points=[(822, 235), (651, 235)])
edge("Stopping", "Stopped", "정지 성공",
     exit=(0, 0.5), entry=(0.5, 1), points=[(157, 296)])
edge("Stopping", "StopFailed", "정지 실패", exit=(1, 0.35), entry=(0, 0.35))
edge("StopFailed", "Stopping", "재시도", exit=(0, 0.65), entry=(1, 0.65))

body = "".join(cells)
xml = ('<mxfile host="app.diagrams.net" agent="Claude" type="device">'
       '<diagram id="runstate" name="Run 상태 머신">'
       '<mxGraphModel grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" '
       'page="1" pageScale="1" pageWidth="980" pageHeight="380" math="0" shadow="0">'
       f'<root><mxCell id="0" /><mxCell id="1" parent="0" />{body}</root></mxGraphModel></diagram></mxfile>')

dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "state.drawio")
with open(dst, "w", encoding="utf-8") as f:
    f.write(xml)
print("wrote", dst)

# -*- coding: utf-8 -*-
"""Generate sequence.drawio for the leveled Run-Lifecycle view.

Pages: Level 1 (개요), 2.1 (준비 공통), 2.2 (모드별 시작), 2.3 (측정 루프),
2.4 (사용자 요청 종료), 2.5 (자연 종료), 2.6 (프로그램 종료 teardown).

Termination is split per trigger so no single page becomes oversized. The
in-diagram title is omitted because the embedding Markdown already supplies each
heading. Self-call labels are offset clear of their loop. Output is written next
to this script.
"""
import os

STEP = 46
TOP = 60
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


def esc(s):
    s = s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")
    return s.replace("\n", "&#10;")


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
        self.y = FIRST_Y
        self.act = {p[0]: [] for p in participants}
        self.last_y = FIRST_Y
        self._n = 0

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

    def note(self, label, li=None, ri=None):
        left, right = self.frame_span(li, ri)
        y = self.y
        self.texts.append(
            f'<mxCell id="{self.nid("note")}" value="{esc(label)}" style="shape=note;whiteSpace=wrap;html=1;'
            f'fillColor=#fef9c3;strokeColor=#ca8a04;fontColor=#0f172a;fontSize=11;align=center;verticalAlign=middle;size=10;" '
            f'vertex="1" parent="1"><mxGeometry x="{left+10}" y="{y}" width="{right-left-20}" height="34" as="geometry" /></mxCell>')
        self.y += 50

    def ref(self, caption, li=None, ri=None):
        left, right = self.frame_span(li, ri)
        top = self.y
        self.frames.append(
            f'<mxCell id="{self.nid("ref")}" value="ref" style="shape=umlFrame;whiteSpace=wrap;html=1;fillColor=none;'
            f'strokeColor={C_REF};fontColor=#0f172a;fontSize=12;fontStyle=1;width=46;height=22;verticalAlign=top;align=left;" '
            f'vertex="1" parent="1"><mxGeometry x="{left}" y="{top}" width="{right-left}" height="50" as="geometry" /></mxCell>')
        self.texts.append(
            f'<mxCell id="{self.nid("refc")}" value="{esc(caption)}" style="text;html=1;strokeColor=none;fillColor=none;'
            f'align=center;verticalAlign=middle;fontSize=12;fontColor=#0f172a;" vertex="1" parent="1">'
            f'<mxGeometry x="{left+10}" y="{top+16}" width="{right-left-20}" height="32" as="geometry" /></mxCell>')
        self.y = top + 64

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
                self.y += 6
            if guard:
                self.texts.append(
                    f'<mxCell id="{self.nid("g")}" value="{esc("[" + guard + "]")}" style="text;html=1;strokeColor=none;'
                    f'fillColor=none;align=left;verticalAlign=middle;fontSize=11;fontColor=#0f172a;fontStyle=2;" vertex="1" parent="1">'
                    f'<mxGeometry x="{left+24}" y="{self.y}" width="{right-left-40}" height="20" as="geometry" /></mxCell>')
                self.y += 40
            fn(self)
            self.y += 6
        bottom = self.y + 6
        self.frames.append(
            f'<mxCell id="{self.nid("frag")}" value="{kind}" style="shape=umlFrame;whiteSpace=wrap;html=1;fillColor=none;'
            f'strokeColor={C_FRAME};fontColor=#0f172a;fontSize=12;fontStyle=1;width=54;height=24;verticalAlign=top;align=left;" '
            f'vertex="1" parent="1"><mxGeometry x="{left}" y="{top}" width="{right-left}" height="{bottom-top}" as="geometry" /></mxCell>')
        self.y = bottom + 12

    def build_lifelines(self):
        lh = self.y - TOP + 20
        for i, (key, label, is_actor) in enumerate(self.participants):
            x = 40 + GAP * i
            extra = "participant=umlActor;" if is_actor else ""
            fill = "none" if is_actor else C_LIFEFILL
            self.lifelines.append(
                f'<mxCell id="{self.pid}_{key}" value="{esc(label)}" style="shape=umlLifeline;{extra}'
                f'perimeter=lifelinePerimeter;whiteSpace=wrap;html=1;container=1;collapsible=0;recursiveResize=0;'
                f'outlineConnect=0;size=48;fillColor={fill};strokeColor={C_LIFE};fontColor=#0f172a;fontSize=12;" '
                f'vertex="1" parent="1"><mxGeometry x="{x}" y="{TOP}" width="{LW}" height="{lh}" as="geometry" /></mxCell>')

    def xml(self):
        self.build_lifelines()
        pw = self.page_w()
        ph = self.y + 40
        body = "".join(self.lifelines + self.frames + self.bars + self.msgs + self.texts)
        return (f'<diagram id="{self.pid}" name="{esc(self.name)}">'
                f'<mxGraphModel grid="1" gridSize="10" guides="1" tooltips="1" connect="1" arrows="1" fold="1" '
                f'page="1" pageScale="1" pageWidth="{int(pw)}" pageHeight="{int(ph)}" math="0" shadow="0">'
                f'<root><mxCell id="0" /><mxCell id="1" parent="0" />{body}</root></mxGraphModel></diagram>')


# ---- termination flows (each is one page; no outer alt) ----
def user_stop_body(pg):
    # Shared "immediate stop" sequence; App is already activated by the caller.
    pg.selfmsg("App", "RunState = Stopping")
    pg.msg("App", "Sess", "StopInputWorker(CurrentMode)")
    pg.act_on("Sess"); pg.msg("Sess", "Input", "TryStop(timeout)")
    pg.act_on("Input"); pg.msg("Input", "Sess", "stopped", "ret"); pg.act_off("Input")
    pg.msg("Sess", "App", "input stopped", "ret"); pg.act_off("Sess")
    pg.msg("App", "Sess", "StopAnalysisThread()")
    pg.act_on("Sess"); pg.msg("Sess", "Analysis", "TryStop(timeout)")
    pg.act_on("Analysis"); pg.msg("Analysis", "Sess", "stopped", "ret"); pg.act_off("Analysis")
    pg.msg("Sess", "App", "analysis stopped", "ret"); pg.act_off("Sess")
    pg.msg("App", "Sess", "InvalidateRunSession()")
    pg.act_on("Sess"); pg.msg("Sess", "App", "token advanced", "ret"); pg.act_off("Sess")
    pg.frag("opt", [("Playback 또는 Simulation이면",
                     lambda q: q.selfmsg("App", "RestorePlaybackOrSimulationAudioState()"))])
    pg.selfmsg("App", 'RunState = Stopped / Status = "Stopped" 또는 "Reset"')
    pg.act_off("App")


def natural_end(pg):
    pg.msg("Input", "App", "DoneReadingFile (WAV EOF)", "ret")
    pg.act_on("App")
    pg.msg("App", "Sess", "IsCurrentRunSession(token)")
    pg.act_on("Sess"); pg.msg("Sess", "App", "current", "ret"); pg.act_off("Sess")
    pg.msg("App", "Sess", "InvalidateRunSession()")
    pg.act_on("Sess"); pg.msg("Sess", "App", "token advanced", "ret"); pg.act_off("Sess")
    pg.selfmsg("App", "RunState = Stopping")
    pg.selfmsg("App", "RestorePlaybackOrSimulationAudioState()")
    pg.msg("App", "Sess", "StopInputWorker(Playback)")
    pg.act_on("Sess"); pg.msg("Sess", "Input", "TryStop(timeout)")
    pg.act_on("Input"); pg.msg("Input", "Sess", "stopped", "ret"); pg.act_off("Input")
    pg.msg("Sess", "App", "input stopped", "ret"); pg.act_off("Sess")
    pg.msg("App", "Sess", "StopAnalysisThread(completeInput: true)")
    pg.act_on("Sess"); pg.msg("Sess", "Analysis", "CompleteInput(timeout)")
    pg.act_on("Analysis"); pg.msg("Analysis", "Core", "DrainAndFlushInput()")
    pg.act_on("Core"); pg.msg("Core", "Analysis", "final projected data", "ret"); pg.act_off("Core")
    pg.msg("Analysis", "App", "final AnalysisFrameReady(frame)", "ret")
    pg.msg("App", "Analysis", "final frame accepted", "ret")
    pg.msg("Analysis", "Sess", "completed", "ret"); pg.act_off("Analysis")
    pg.msg("Sess", "App", "analysis completed", "ret"); pg.act_off("Sess")
    pg.selfmsg("App", 'RunState = Stopped / Status = "Stopped"')
    pg.act_off("App")


def program_close(pg):
    pg.selfmsg("App", "OnWindowClosed()")
    pg.act_on("App")
    pg.msg("App", "Sess", "InvalidateRunSession()")
    pg.act_on("Sess"); pg.msg("Sess", "App", "token advanced", "ret"); pg.act_off("Sess")
    pg.msg("App", "Sess", 'StopInputWorker("Input")')
    pg.act_on("Sess"); pg.msg("Sess", "Input", "TryStop(timeout)")
    pg.act_on("Input"); pg.msg("Input", "Sess", "stopped", "ret"); pg.act_off("Input")
    pg.msg("Sess", "App", "input stopped", "ret"); pg.act_off("Sess")
    pg.msg("App", "Sess", "StopAnalysisThread()")
    pg.act_on("Sess"); pg.msg("Sess", "Analysis", "TryStop(timeout)")
    pg.act_on("Analysis"); pg.msg("Analysis", "Sess", "stopped", "ret"); pg.act_off("Analysis")
    pg.msg("Sess", "App", "analysis stopped", "ret"); pg.act_off("Sess")
    pg.msg("App", "User", "프로세스 종료", "ret")
    pg.act_off("App")


pages = []

# Level 1 · 실행 수명주기 개요
p = Page("level1", "Level 1 · 실행 수명주기 개요",
         [("User", "User", True),
          ("App", "App layer\nMainWindow/ViewModel/RunCommandService", False),
          ("Sess", "RunSessionController", False),
          ("Input", "Input worker\nLive/Playback/Simulation", False),
          ("Analysis", "AnalysisWorker", False),
          ("Core", "Core pipeline\nDetection/Metrics/Projectors", False)])
p.msg("User", "App", "프로그램 실행")
p.act_on("App"); p.selfmsg("App", "UI·command·service 구성,\n입력 소스 목록 구성"); p.act_off("App")
p.msg("User", "App", "Start 선택")
p.act_on("App")
p.selfmsg("App", "RunState = Starting / CurrentMode 결정")
p.ref("Level 2.1 · 입력 실행 준비 공통 흐름", 1, 5)
p.ref("Level 2.2 · 입력 모드별 시작 흐름", 1, 5)
p.selfmsg("App", 'RunState = Running / Status = "Running"')
p.act_off("App")
p.ref("Level 2.3 · 측정 중 분석 반복 흐름", 1, 5)
p.ref("Level 2.4 / 2.5 · 측정 종료", 0, 5)
p.msg("User", "App", "프로그램 종료")
p.act_on("App")
p.ref("Level 2.6 · 프로그램 종료 teardown", 1, 5)
p.msg("App", "User", "프로세스 종료", "ret")
p.act_off("App")
pages.append(p)

# Level 2.1 · 입력 실행 준비 공통 흐름
p = Page("level21", "Level 2.1 · 입력 실행 준비 공통 흐름",
         [("App", "App layer", False), ("Sess", "RunSessionController", False),
          ("Buffer", "MasterAudioBuffer", False), ("Analysis", "AnalysisWorker", False)])
p.msg("App", "Sess", "PrepareInputRun(sampleRate)")
p.act_on("Sess")
p.selfmsg("Sess", "runSessionToken 발급")
p.msg("Sess", "Buffer", "« create » MasterAudioBuffer(sampleRate)", "ret")
p.act_on("Buffer"); p.msg("Buffer", "Sess", "buffer", "ret"); p.act_off("Buffer")
p.msg("Sess", "Analysis", "« create » AnalysisWorker(buffer, config)", "ret")
p.act_on("Analysis"); p.msg("Analysis", "Sess", "analysis worker", "ret"); p.act_off("Analysis")
p.msg("Sess", "Analysis", "Start()")
p.act_on("Analysis"); p.msg("Analysis", "Sess", "analysis thread started", "ret"); p.act_off("Analysis")
p.msg("Sess", "App", "buffer + runSessionToken", "ret")
p.act_off("Sess")
pages.append(p)

# Level 2.2 · 입력 모드별 시작 흐름
p = Page("level22", "Level 2.2 · 입력 모드별 시작 흐름",
         [("User", "User", True), ("App", "App layer", False),
          ("Input", "Input worker\nLive/Playback/Simulation", False)])
p.act_on("App")

def live(pg):
    pg.selfmsg("App", "live backend 설정 / device·rate·gain 검증")
    pg.msg("App", "Input", "create live worker / Start(device, sampleRate, gain)")
    pg.act_on("Input"); pg.msg("Input", "App", "capture thread started", "ret"); pg.act_off("Input")

def playback(pg):
    pg.msg("App", "User", "WAV 파일 선택 요청")
    pg.msg("User", "App", "WAV 파일 선택", "ret")
    pg.selfmsg("App", "이전 live audio 상태 저장 / Playback source·rate 적용")
    pg.msg("App", "Input", "« create » PlaybackWorker(buffer, rate) / Start(filePath)", "ret")
    pg.act_on("Input"); pg.msg("Input", "App", "playback thread started", "ret"); pg.act_off("Input")

def sim(pg):
    pg.selfmsg("App", "WatchSynthStreamConfig 구성 / Simulation source·rate 적용")
    pg.msg("App", "Input", "« create » SimWorker(buffer, rate) / Start(config)", "ret")
    pg.act_on("Input"); pg.msg("Input", "App", "sim thread started", "ret"); pg.act_off("Input")

p.frag("alt", [("Live mode", live), ("Playback mode", playback), ("Simulation mode", sim)])
p.act_off("App")
pages.append(p)

# Level 2.3 · 측정 중 분석 반복 흐름
p = Page("level23", "Level 2.3 · 측정 중 분석 반복 흐름",
         [("App", "App layer", False), ("Sess", "RunSessionController", False),
          ("Input", "Input worker", False), ("Buffer", "MasterAudioBuffer", False),
          ("Analysis", "AnalysisWorker", False), ("Core", "Core pipeline", False)])

def loopbody(pg):
    pg.msg("Input", "Buffer", "WriteSamples(block)")
    pg.act_on("Buffer"); pg.msg("Buffer", "Input", "samples stored", "ret"); pg.act_off("Buffer")
    pg.msg("Input", "Sess", "DataReady", "ret")
    pg.act_on("Sess")
    pg.selfmsg("Sess", "runSessionToken 확인")
    pg.msg("Sess", "Analysis", "NotifyDataReady()")
    pg.act_on("Analysis"); pg.msg("Analysis", "Sess", "wakeup signaled", "ret"); pg.act_off("Sess")
    pg.msg("Analysis", "Buffer", "CopyAnalysisSamples()")
    pg.act_on("Buffer"); pg.msg("Buffer", "Analysis", "analysis block", "ret"); pg.act_off("Buffer")
    pg.msg("Analysis", "Core", "Process(block)")
    pg.act_on("Core"); pg.msg("Core", "Analysis", "detection / metrics / projected frame data", "ret"); pg.act_off("Core")
    pg.msg("Analysis", "App", "AnalysisFrameReady(frame)", "ret")
    pg.act_on("App")
    pg.selfmsg("App", "최신 frame으로 UI 상태/화면 갱신")
    pg.msg("App", "Analysis", "frame accepted", "ret")
    pg.act_off("App"); pg.act_off("Analysis")

p.frag("loop", [("측정 중: 입력 block마다 — Live·Simulation은 정지까지 무한 반복, Playback은 WAV EOF까지", loopbody)])
pages.append(p)

# Level 2.4 · 측정 종료 (사용자 요청 / 외부 비정상 종료)
# Two triggers converge on the same immediate-stop sequence (plain TryStop, no
# input drain): a user Stop/Reset request (any mode; for Simulation this is the
# only way its infinite loop ends), or Live capture dying on its own
# (CaptureEnded -> HandleLiveCaptureEnded -> StopRunAndRefreshDevices).
p = Page("level24", "Level 2.4 · 측정 종료 (사용자 요청 / 외부 비정상 종료)",
         [("User", "User", True), ("App", "App layer", False), ("Sess", "RunSessionController", False),
          ("Input", "Input worker", False), ("Analysis", "AnalysisWorker", False)])
p.frag("alt", [
    ("사용자가 측정 종료를 요청한 경우 (Live/Playback/Simulation)",
     lambda q: q.msg("User", "App", "Pause 후 Reset 또는 내부 stop 요청")),
    ("외부 비정상 종료된 경우 (Live capture 끊김 등)",
     lambda q: q.msg("Input", "App", "CaptureEnded (runSessionToken 일치 시 정지)", "ret")),
])
p.last_y = p.y  # start the activation bar just below the trigger alt
p.act_on("App")
user_stop_body(p)
pages.append(p)

# Level 2.5 · Playback/Simulation 자연 종료
p = Page("level25", "Level 2.5 · Playback 자연 종료",
         [("App", "App layer", False), ("Sess", "RunSessionController", False),
          ("Input", "Input worker", False), ("Analysis", "AnalysisWorker", False),
          ("Core", "Core pipeline", False)])
natural_end(p)
pages.append(p)

# Level 2.6 · 프로그램 종료 teardown
p = Page("level26", "Level 2.6 · 프로그램 종료 teardown",
         [("User", "User", True), ("App", "App layer", False), ("Sess", "RunSessionController", False),
          ("Input", "Input worker", False), ("Analysis", "AnalysisWorker", False)])
program_close(p)
pages.append(p)

out = ('<mxfile host="app.diagrams.net" agent="Claude" type="device">'
       + "".join(pg.xml() for pg in pages) + "</mxfile>")
dst = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sequence.drawio")
with open(dst, "w", encoding="utf-8") as f:
    f.write(out)
print("wrote", dst, "pages:", len(pages))

"""Draws the 'real' tooth view, the AR (virtual) overlay and the twin dashboard."""
import math

import numpy as np
import pygame

from twin import ACCESS, WORKING_LENGTH, SHAPING, OBTURATION, COMPLETE, FAILED, STAGES

W, H = 1280, 820
VIEW_W = 700
SCALE = 21.0  # px per mm

BG = (14, 17, 22)
PANEL = (22, 27, 35)
PANEL_2 = (30, 36, 46)
TEXT = (225, 230, 238)
MUTED = (140, 150, 165)
AR_CYAN = (60, 220, 240)
AR_GREEN = (80, 230, 140)
AR_AMBER = (255, 190, 60)
AR_RED = (255, 80, 80)
LEVEL_COL = [AR_CYAN, AR_AMBER, AR_RED, AR_RED]

ISO_COLORS = {10: (160, 90, 200), 15: (240, 240, 240), 20: (240, 210, 40), 25: (220, 50, 50),
              30: (50, 110, 230), 35: (40, 170, 80), 40: (40, 40, 40)}


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


class Renderer:
    def __init__(self, screen):
        self.s = screen
        f = pygame.font.SysFont
        self.f_title = f("segoeui", 22, bold=True)
        self.f_h = f("segoeui", 16, bold=True)
        self.f = f("segoeui", 15)
        self.f_small = f("segoeui", 13)
        self.f_mono = f("consolas", 14)
        self.f_big = f("segoeui", 40, bold=True)
        self.base = None
        self.base_origin = (VIEW_W // 2, 140)

    # ------------------------------------------------------------------
    def build_base(self, p):
        """Pre-render the static 'real' anatomy (bone, PDL, dentin, enamel)."""
        surf = pygame.Surface((VIEW_W, H), pygame.SRCALPHA)
        ox, oy = self.base_origin
        tp = lambda pt: (ox + pt[0] * SCALE, oy + pt[1] * SCALE)

        # Alveolar bone with a trabecular texture
        rng = np.random.default_rng(p.seed)
        bone_top = oy + (p.crown_length + 1.8) * SCALE
        bone = pygame.Rect(40, int(bone_top), VIEW_W - 80, int(H - bone_top - 70))
        pygame.draw.rect(surf, (78, 74, 70), bone, border_radius=26)
        for _ in range(900):
            x = rng.integers(bone.left + 8, bone.right - 8)
            y = rng.integers(bone.top + 8, bone.bottom - 8)
            r = int(rng.integers(2, 7))
            c = int(rng.integers(55, 105))
            pygame.draw.circle(surf, (c, c - 3, c - 6), (int(x), int(y)), r, 1)
        # Gingiva
        pygame.draw.rect(surf, (150, 70, 80), (40, int(bone_top - 26), VIEW_W - 80, 40), border_radius=16)

        # Periodontal ligament (dark halo) then dentin
        pygame.draw.polygon(surf, (35, 32, 30), [tp(q) for q in p.outline(extra=0.25)])
        pygame.draw.polygon(surf, (206, 198, 180), [tp(q) for q in p.outline()])
        pygame.draw.polygon(surf, (248, 246, 236), [tp(q) for q in p.enamel_outline()])
        # dentin inside crown (enamel ~1.2 mm thick)
        inner = []
        for q in p.enamel_outline():
            d = q[1]
            if d < 1.2:
                continue
            inner.append((math.copysign(max(0.3, abs(q[0]) - 1.1), q[0]), d))
        if len(inner) > 3:
            pygame.draw.polygon(surf, (206, 198, 180), [tp(q) for q in inner])
        # Cementoenamel junction line
        y = oy + p.crown_length * SCALE
        hw = p.half_width(p.crown_length) * SCALE
        pygame.draw.line(surf, (170, 160, 140), (ox - hw, y), (ox + hw, y), 1)
        self.base = surf

    def blit_base(self, pose):
        ox, oy = self.base_origin
        self.s.blit(self.base, (pose.x - ox, pose.y - oy))

    # ------------------------------------------------------------------
    def draw_real(self, twin, pose):
        p = twin.p
        self.blit_base(pose)
        tp = pose.to_screen

        # Access cavity (removed tooth structure)
        if twin.access_depth > 0:
            a = min(twin.access_depth, p.chamber_roof + 0.2)
            hw = 1.3
            poly = [(-hw, -0.1), (hw, -0.1), (hw * 0.9, a), (-hw * 0.9, a)]
            pygame.draw.polygon(self.s, (40, 30, 30), [tp(q) for q in poly])

        # Pulp chamber + canal: vital pulp (red) fades to cleaned canal (dark)
        i0 = p.index(p.chamber_roof)
        i1 = p.index(p.apex)
        step = 2
        for i in range(i0, i1, step):
            j = min(i + step, i1)
            d0, d1 = p.depths[i], p.depths[j]
            r0, r1 = twin.shaped_radius[i], twin.shaped_radius[j]
            if r0 <= 0 and r1 <= 0:
                continue
            c0, c1 = p.centerline_x(d0), p.centerline_x(d1)
            clean = twin.cleaned[i]
            col = lerp((170, 50, 60), (30, 24, 26), clean)
            if twin.debris > 5 and twin.stage == SHAPING and d0 > p.chamber_floor:
                col = lerp(col, (120, 110, 80), min(0.6, twin.debris / 150))
            quad = [(c0 - r0, d0), (c0 + r0, d0), (c1 + r1, d1), (c1 - r1, d1)]
            pygame.draw.polygon(self.s, col, [tp(q) for q in quad])

        # Gutta-percha obturation (fills from WL upward)
        if twin.fill_level > 0:
            top = twin.wl - twin.fill_level * (twin.wl - p.chamber_floor)
            left, right = [], []
            for i in range(p.index(top), p.index(twin.wl) + 1):
                d = p.depths[i]
                r = twin.shaped_radius[i]
                cx = p.centerline_x(d)
                left.append(tp((cx - r, d)))
                right.append(tp((cx + r, d)))
            if len(left) > 1:
                pygame.draw.polygon(self.s, (245, 150, 90), left + right[::-1])

        # Irrigation (NaOCl bubbles)
        if twin.irrigating and twin.stage != ACCESS:
            t = pygame.time.get_ticks() / 1000
            for k in range(14):
                d = p.chamber_floor + ((k * 1.7 + t * 6) % max(1, twin.wl - p.chamber_floor))
                r = twin.shaped_radius[p.index(d)]
                x = p.centerline_x(d) + math.sin(k + t * 5) * r * 0.6
                pygame.draw.circle(self.s, (200, 235, 255), tp((x, d)), 2)

        self.draw_instrument(twin, pose)

    def draw_instrument(self, twin, pose):
        p, tp = twin.p, pose.to_screen
        if twin.stage in (COMPLETE,) or (twin.stage == OBTURATION):
            return
        tip_d = twin.depth
        if twin.instrument == "Bur":
            shaft_top = tp((0, -9))
            tip = tp((0, tip_d))
            pygame.draw.line(self.s, (175, 180, 190), shaft_top, (tip[0], tip[1] - 12), 7)
            pygame.draw.circle(self.s, (210, 200, 120), (int(tip[0]), int(tip[1] - 6)), 9)
            pygame.draw.circle(self.s, (120, 110, 60), (int(tip[0]), int(tip[1] - 6)), 9, 1)
            return
        # K-file: follows canal centreline, 0.02 taper, ISO colour handle
        pts = []
        d = tip_d
        while d > tip_d - 16 and d > -2:
            x = p.centerline_x(d) if d > 0 else 0.0
            r = twin.file_tip_radius() + 0.01 * (tip_d - d)
            pts.append((x, d, r))
            d -= 0.3
        for (x0, d0, r0), (x1, d1, _) in zip(pts, pts[1:]):
            w = max(2, int(r0 * 2 * SCALE))
            pygame.draw.line(self.s, (220, 225, 235), tp((x0, d0)), tp((x1, d1)), w)
        shank_top = tip_d - 16
        pygame.draw.line(self.s, (190, 195, 205), tp((0, min(shank_top, -1))), tp((0, -6)), 4)
        hx, hy = tp((0, -6))
        col = ISO_COLORS.get(twin.file_size, (200, 200, 200))
        pygame.draw.rect(self.s, col, (hx - 10, hy - 34, 20, 34), border_radius=4)
        pygame.draw.rect(self.s, (0, 0, 0), (hx - 10, hy - 34, 20, 34), 1, border_radius=4)
        # Rubber stop set at recorded WL
        if twin.recorded_wl is not None:
            sy = tip_d - twin.recorded_wl
            if -6 < sy < tip_d:
                sx, syy = tp((0, sy))
                pygame.draw.ellipse(self.s, (230, 90, 60), (sx - 9, syy - 3, 18, 6))

    # ------------------------------------------------------------------
    def draw_ar(self, twin, pose, true_pose, engine, reg_err):
        """Virtual overlays drawn through the *tracked* pose (Azuma property 3)."""
        p, tp = twin.p, pose.to_screen
        ov = pygame.Surface((VIEW_W, H), pygame.SRCALPHA)
        alpha = 230 if reg_err <= 0.5 else 120
        a = lambda c, k=1.0: (*c, int(alpha * k))

        # Fiducials: real markers (white) vs virtual registration crosshairs (cyan)
        for fx in (-p.crown_width / 2 - 1.2, p.crown_width / 2 + 1.2):
            rx, ry = true_pose.to_screen((fx, 1.0))
            pygame.draw.circle(self.s, (250, 250, 250), (int(rx), int(ry)), 4)
            vx, vy = tp((fx, 1.0))
            pygame.draw.circle(ov, a(AR_CYAN), (int(vx), int(vy)), 8, 1)
            pygame.draw.line(ov, a(AR_CYAN), (vx - 12, vy), (vx + 12, vy), 1)
            pygame.draw.line(ov, a(AR_CYAN), (vx, vy - 12), (vx, vy + 12), 1)

        # Canal centreline path (dashed)
        d = p.chamber_floor
        k = 0
        while d < p.apex - 0.2:
            if k % 2 == 0:
                pygame.draw.line(ov, a(AR_CYAN, 0.7), tp((p.centerline_x(d), d)),
                                 tp((p.centerline_x(d + 0.4), d + 0.4)), 2)
            d += 0.4
            k += 1

        def hline(depth, col, label, width=2, span=2.8):
            cx = p.centerline_x(depth)
            x0, y0 = tp((cx - span, depth))
            x1, _ = tp((cx + span, depth))
            pygame.draw.line(ov, a(col), (x0, y0), (x1, y0), width)
            if label:
                img = self.f_small.render(label, True, col)
                ov.blit(img, (x1 + 6, y0 - 9))

        if twin.stage == ACCESS:
            hline(p.chamber_roof, AR_GREEN, "Pulp roof")
            hline(p.chamber_floor, AR_RED, "Chamber floor — do not cross")
        else:
            # Apical safe zone band (0–1 mm short of apex)
            band = []
            for dd in np.linspace(p.apex - 1.0, p.apex, 6):
                band.append((p.centerline_x(dd) - 1.4, dd))
            for dd in np.linspace(p.apex, p.apex - 1.0, 6):
                band.append((p.centerline_x(dd) + 1.4, dd))
            pygame.draw.polygon(ov, (80, 230, 140, 60), [tp(q) for q in band])
            hline(p.apex, AR_RED, "Radiographic apex", 2)
            if twin.recorded_wl is not None:
                hline(twin.recorded_wl, AR_AMBER, f"WL {twin.recorded_wl:.1f} mm", 2, 2.0)
            if p.curvature_deg > 12:
                cx, cy = tp((p.centerline_x(p.curve_start), p.curve_start))
                pygame.draw.arc(ov, a(AR_AMBER, 0.8), (cx - 30, cy - 5, 60, 60), 0.3, 2.8, 2)

        # Instrument tip marker + live distance label
        if twin.stage in (ACCESS, WORKING_LENGTH, SHAPING):
            tx, ty = tp((p.centerline_x(twin.depth) if twin.depth > 0 else 0, twin.depth))
            col = AR_GREEN
            if twin.stage != ACCESS:
                gap = twin.wl - twin.depth if twin.stage == SHAPING else p.target_wl - twin.depth
                if gap < -0.3:
                    col = AR_RED
                elif gap < 1.0:
                    col = AR_AMBER
                lbl = f"{gap:+.1f} mm to {'WL' if twin.stage == SHAPING else 'target'}"
            else:
                lbl = f"depth {twin.depth:.1f} mm"
            pygame.draw.circle(ov, a(col), (int(tx), int(ty)), 10, 2)
            img = self.f_small.render(lbl, True, col)
            ov.blit(img, (tx - img.get_width() - 16, ty - 8))

        self.s.blit(ov, (0, 0))

        # Adaptive prompt callout anchored to anatomy
        pr = engine.current
        if pr:
            text = engine.text(pr, twin)
            col = LEVEL_COL[pr.level]
            lines = self.wrap(text, self.f, 300)
            bw = max(self.f.size(l)[0] for l in lines) + 24
            bh = len(lines) * 20 + 30
            bx, by = 18, 60
            box = pygame.Surface((bw, bh), pygame.SRCALPHA)
            box.fill((10, 20, 28, 215))
            self.s.blit(box, (bx, by))
            pygame.draw.rect(self.s, col, (bx, by, bw, bh), 2, border_radius=6)
            tag = "AR PROMPT · " + engine.detail_level(twin).upper()
            self.s.blit(self.f_small.render(tag, True, col), (bx + 12, by + 5))
            for i, l in enumerate(lines):
                self.s.blit(self.f.render(l, True, TEXT), (bx + 12, by + 24 + i * 20))
            if pr.anchor is not None:
                ax, ay = tp(pr.anchor)
                pygame.draw.line(self.s, col, (bx + bw, by + bh // 2), (ax, ay), 1)
                pygame.draw.circle(self.s, col, (int(ax), int(ay)), 4)

    # ------------------------------------------------------------------
    def wrap(self, text, font, width):
        words, lines, cur = text.split(), [], ""
        for w in words:
            t = (cur + " " + w).strip()
            if font.size(t)[0] > width and cur:
                lines.append(cur)
                cur = w
            else:
                cur = t
        if cur:
            lines.append(cur)
        return lines

    def bar(self, x, y, w, label, value, maxv, col, suffix="%"):
        self.s.blit(self.f_small.render(label, True, MUTED), (x, y))
        vtxt = self.f_small.render(f"{value:.0f}{suffix}" if suffix else f"{value}", True, TEXT)
        self.s.blit(vtxt, (x + w - vtxt.get_width(), y))
        pygame.draw.rect(self.s, PANEL_2, (x, y + 18, w, 8), border_radius=4)
        frac = max(0.0, min(1.0, value / maxv))
        pygame.draw.rect(self.s, col, (x, y + 18, int(w * frac), 8), border_radius=4)

    def draw_dashboard(self, twin, tracker, engine, fps, ar_on):
        p = twin.p
        x0 = VIEW_W
        pygame.draw.rect(self.s, PANEL, (x0, 0, W - x0, H))
        x, y, w = x0 + 22, 14, W - x0 - 44

        self.s.blit(self.f_h.render("DIGITAL SURGICAL TWIN · live state", True, AR_CYAN), (x, y))
        y += 26
        self.s.blit(self.f.render(p.spec.label, True, TEXT), (x, y))
        y += 20
        info = (f"Patient #{p.seed}  ·  canal {p.canal_length:.2f} mm  ·  curvature {p.curvature_deg:.0f}°  ·  "
                f"target WL {p.target_wl:.2f} mm")
        self.s.blit(self.f_small.render(info, True, MUTED), (x, y))
        y += 26

        # Stage stepper
        sw = w // 4
        cur = STAGES.index(twin.stage) if twin.stage in STAGES else (4 if twin.stage == COMPLETE else -1)
        for i, st in enumerate(STAGES):
            col = AR_GREEN if i < cur or twin.stage == COMPLETE else AR_CYAN if i == cur else PANEL_2
            if twin.stage == FAILED:
                col = AR_RED if st == getattr(self, "_last_stage", st) else PANEL_2
            pygame.draw.rect(self.s, col, (x + i * sw, y, sw - 6, 6), border_radius=3)
            self.s.blit(self.f_small.render(f"{i + 1}. {st}", True, TEXT if i <= cur else MUTED),
                        (x + i * sw, y + 10))
        if twin.stage != FAILED:
            self._last_stage = twin.stage
        y += 38

        # Instrument + apex locator
        pygame.draw.rect(self.s, PANEL_2, (x, y, w, 88), border_radius=8)
        inst = twin.instrument if twin.instrument != "K-file" or twin.stage == WORKING_LENGTH else "K-file"
        size = f"ISO #{twin.file_size}" if twin.instrument == "K-file" else ""
        self.s.blit(self.f_h.render(f"{inst} {size}", True, TEXT), (x + 12, y + 8))
        self.s.blit(self.f_mono.render(f"depth {twin.depth:6.2f} mm", True, TEXT), (x + 12, y + 34))
        wl_txt = f"WL    {twin.recorded_wl:6.2f} mm" if twin.recorded_wl is not None else "WL    --"
        self.s.blit(self.f_mono.render(wl_txt, True, TEXT), (x + 12, y + 56))
        # Apex locator gauge
        gx, gw = x + 230, w - 245
        self.s.blit(self.f_small.render("Electronic apex locator", True, MUTED), (gx, y + 8))
        loc = twin.apex_locator()
        pygame.draw.rect(self.s, BG, (gx, y + 32, gw, 18), border_radius=4)
        for mm in range(0, 4):
            tx = gx + gw - int(gw * mm / 3)
            pygame.draw.line(self.s, MUTED, (tx, y + 50), (tx, y + 56))
            self.s.blit(self.f_small.render(str(mm) if mm else "APEX", True, MUTED), (tx - 12, y + 58))
        if loc is not None:
            frac = 1 - max(0.0, min(3.0, loc)) / 3
            col = AR_GREEN if 0.2 <= loc <= 1.0 else AR_RED if loc < 0.2 else AR_CYAN
            pygame.draw.rect(self.s, col, (gx, y + 32, int(gw * frac), 18), border_radius=4)
            self.s.blit(self.f_mono.render("APEX" if loc <= 0 else f"{loc:.1f}", True, BG if frac > 0.2 else TEXT),
                        (gx + 6, y + 33))
        y += 100

        # Live bars
        half = (w - 20) // 2
        self.bar(x, y, half, "File stress (separation at 100)", twin.stress, 100,
                 AR_RED if twin.stress > 70 else AR_AMBER if twin.stress > 40 else AR_GREEN)
        self.bar(x + half + 20, y, half, "Canal debris", twin.debris, 100,
                 AR_AMBER if twin.debris > 60 else AR_CYAN)
        y += 38
        self.bar(x, y, half, "Canal cleanliness", twin.cleanliness() * 100, 100, AR_GREEN)
        self.bar(x + half + 20, y, half, "Obturation fill", twin.fill_level * 100, 100, (245, 150, 90))
        y += 42

        # Files
        self.s.blit(self.f_small.render("File sequence", True, MUTED), (x, y))
        fx = x + 100
        seq = [s for s in [15, 20, 25, 30, 35, 40] if s <= twin.maf]
        for s in seq:
            done = s in twin.files_done
            curr = twin.stage == SHAPING and s == twin.file_size
            pygame.draw.circle(self.s, ISO_COLORS[s], (fx, y + 9), 9)
            pygame.draw.circle(self.s, AR_GREEN if done else AR_CYAN if curr else MUTED, (fx, y + 9), 11, 2)
            self.s.blit(self.f_small.render(str(s), True, TEXT), (fx - 7, y + 22))
            fx += 44
        y += 48

        # Depth-vs-time chart
        self.draw_chart(twin, x, y, w, 150)
        y += 170

        # AR / registration status (Azuma)
        pygame.draw.rect(self.s, PANEL_2, (x, y, w, 96), border_radius=8)
        self.s.blit(self.f_h.render("AR engine (Azuma properties)", True, AR_CYAN), (x + 12, y + 6))
        err = tracker.registration_error_mm()
        rows = [
            ("1 Real + virtual", "ON" if ar_on else "OFF (A)", AR_GREEN if ar_on else MUTED),
            ("2 Real-time", f"{fps:4.0f} fps · {1000 / max(fps, 1):4.1f} ms/frame", AR_GREEN if fps > 30 else AR_AMBER),
            ("3 Registered", f"error {err:.2f} mm" + (" · latency 250 ms" if tracker.degraded else ""),
             AR_GREEN if err <= 0.5 else AR_RED),
        ]
        for i, (k, v, c) in enumerate(rows):
            self.s.blit(self.f_small.render(k, True, MUTED), (x + 12, y + 30 + i * 20))
            self.s.blit(self.f_small.render(v, True, c), (x + 130, y + 30 + i * 20))
        mode = f"Prompts: {engine.detail_level(twin)}{' (auto)' if engine.expert and twin.errors >= 3 else ''}"
        self.s.blit(self.f_small.render(mode, True, TEXT), (x + w - 190, y + 30))
        self.s.blit(self.f_small.render(f"Patient motion: {'ON' if tracker.patient_motion else 'off'}", True, TEXT),
                    (x + w - 190, y + 50))
        self.s.blit(self.f_small.render(f"Score now: {twin.score()}", True, TEXT), (x + w - 190, y + 70))
        y += 106

        # Event log
        self.s.blit(self.f_small.render("Event log", True, MUTED), (x, y))
        y += 18
        for e in twin.events[-5:][::-1]:
            col = [MUTED, AR_AMBER, AR_RED, AR_RED][e.level]
            txt = f"{e.t:6.1f}s  {e.text}"
            while self.f_small.size(txt)[0] > w and len(txt) > 10:
                txt = txt[:-2]
            self.s.blit(self.f_small.render(txt, True, col), (x, y))
            y += 17

    def draw_chart(self, twin, x, y, w, h):
        pygame.draw.rect(self.s, PANEL_2, (x, y, w, h), border_radius=8)
        self.s.blit(self.f_small.render("Instrument depth vs time (last 30 s)", True, MUTED), (x + 10, y + 6))
        rows = twin.log_rows[-300:]
        p = twin.p
        top, bot = y + 26, y + h - 10
        maxd = p.apex + 2
        ymap = lambda d: top + (bot - top) * (d / maxd)
        for d, col, lab in ((p.apex, AR_RED, "apex"), (twin.wl, AR_AMBER, "WL")):
            yy = ymap(d)
            pygame.draw.line(self.s, col, (x + 10, yy), (x + w - 10, yy), 1)
            self.s.blit(self.f_small.render(lab, True, col), (x + w - 40, yy - 16))
        if len(rows) > 1:
            n = 300
            pts = [(x + 10 + (w - 20) * (i + n - len(rows)) / n, ymap(r["depth_mm"])) for i, r in enumerate(rows)]
            pygame.draw.lines(self.s, AR_CYAN, False, pts, 2)

    # ------------------------------------------------------------------
    def draw_header(self, twin):
        pygame.draw.rect(self.s, (10, 12, 16), (0, 0, VIEW_W, 46))
        self.s.blit(self.f_title.render("SurgiFlow Twin — Root Canal DST", True, TEXT), (18, 9))
        st = self.f.render(f"Stage: {twin.stage}", True, AR_CYAN)
        self.s.blit(st, (VIEW_W - st.get_width() - 18, 14))

    def draw_footer(self):
        pygame.draw.rect(self.s, (10, 12, 16), (0, H - 58, VIEW_W, 58))
        l1 = "↑/↓ or wheel: move instrument  ·  SHIFT: fine  ·  ENTER: next stage  ·  W: record WL  ·  N: next file"
        l2 = "I (hold): irrigate  ·  O (hold): obturate  ·  A: AR on/off  ·  R: tracking error  ·  M: patient motion  ·  E: expertise  ·  H: help"
        self.s.blit(self.f_small.render(l1, True, MUTED), (16, H - 50))
        self.s.blit(self.f_small.render(l2, True, MUTED), (16, H - 30))

    def draw_result(self, twin, saved):
        ov = pygame.Surface((VIEW_W, H), pygame.SRCALPHA)
        ov.fill((0, 0, 0, 150))
        self.s.blit(ov, (0, 0))
        bx, by, bw, bh = 90, 200, VIEW_W - 180, 380
        pygame.draw.rect(self.s, PANEL, (bx, by, bw, bh), border_radius=12)
        ok = twin.stage == COMPLETE
        col = AR_GREEN if ok else AR_RED
        pygame.draw.rect(self.s, col, (bx, by, bw, bh), 2, border_radius=12)
        self.s.blit(self.f_big.render("COMPLETE" if ok else "FAILED", True, col), (bx + 24, by + 16))
        self.s.blit(self.f_big.render(f"{twin.score()}/100", True, TEXT), (bx + bw - 190, by + 16))
        yy = by + 84
        lines = []
        if not ok:
            lines.append(twin.fail_reason)
        if twin.wl_error is not None:
            lines.append(f"Working length error: {twin.wl_error:+.2f} mm (target {twin.p.target_wl:.2f} mm)")
        lines += [f"Canal cleanliness: {twin.cleanliness() * 100:.0f}%",
                  f"Peak file stress: {twin.peak_stress:.0f}%",
                  f"Files used: {', '.join('#' + str(s) for s in twin.files_done) or '-'}"]
        for pts, r in twin.penalties[-4:]:
            lines.append(f"  −{pts}  {r}")
        for l in lines:
            for ll in self.wrap(l, self.f, bw - 48):
                self.s.blit(self.f.render(ll, True, TEXT), (bx + 24, yy))
                yy += 22
        if saved:
            self.s.blit(self.f_small.render("Twin log saved to outputs/ (CSV time-series + JSON summary)", True, MUTED),
                        (bx + 24, by + bh - 52))
        self.s.blit(self.f_small.render("F5: new patient  ·  TAB: choose another tooth  ·  ESC: quit", True, AR_CYAN),
                    (bx + 24, by + bh - 30))

    def draw_help(self):
        ov = pygame.Surface((W, H), pygame.SRCALPHA)
        ov.fill((0, 0, 0, 200))
        self.s.blit(ov, (0, 0))
        lines = [
            ("How to perform the root canal", self.f_title, AR_CYAN),
            ("1. ACCESS — drive the bur down (↓) until the pulp roof. Don't hit the chamber floor. ENTER.", self.f, TEXT),
            ("2. WORKING LENGTH — advance the #10 K-file; watch the apex locator. Stop at ~0.5, press W. ENTER.", self.f, TEXT),
            ("3. SHAPING — take each file (#15 → MAF) to WL. Down-strokes build stress; pause to let it relax.", self.f, TEXT),
            ("   Filing makes debris: hold I to irrigate between files, then press N for the next file.", self.f, TEXT),
            ("4. OBTURATION — hold O to condense gutta-percha from WL up to the orifice. ENTER.", self.f, TEXT),
            ("", self.f, TEXT),
            ("Azuma AR demo keys", self.f_h, AR_CYAN),
            ("A  toggle the virtual layer (property 1: real + virtual)", self.f, TEXT),
            ("R  inject tracker latency/bias → watch the overlay drift off the anatomy (property 3: registration)", self.f, TEXT),
            ("M  patient motion (makes latency visible)   ·   E  novice/expert prompts (adaptive)", self.f, TEXT),
            ("P  save screenshot   ·   F5 new patient   ·   TAB tooth menu   ·   ESC quit", self.f, TEXT),
            ("", self.f, TEXT),
            ("Press H to close", self.f_h, AR_AMBER),
        ]
        y = 150
        for t, f, c in lines:
            self.s.blit(f.render(t, True, c), (120, y))
            y += 34 if f is self.f_title else 28

    def draw_menu(self, specs, sel):
        self.s.fill(BG)
        self.s.blit(self.f_big.render("SurgiFlow Twin", True, TEXT), (80, 60))
        self.s.blit(self.f_h.render("Real-time root canal Digital Surgical Twin with adaptive AR prompts (Azuma principles)",
                                    True, AR_CYAN), (82, 115))
        self.s.blit(self.f.render("Choose a tooth — each run samples a new patient from the dataset's mean ± SD:",
                                  True, MUTED), (82, 160))
        y = 200
        for i, sp in enumerate(specs):
            r = pygame.Rect(80, y, W - 160, 64)
            pygame.draw.rect(self.s, PANEL_2 if i == sel else PANEL, r, border_radius=10)
            if i == sel:
                pygame.draw.rect(self.s, AR_CYAN, r, 2, border_radius=10)
            self.s.blit(self.f_h.render(f"{i + 1}.  {sp.label}", True, TEXT), (100, y + 8))
            det = (f"canal {sp.mean_length:.1f} ± {sp.sd_length:.1f} mm  ·  curvature ~{sp.curvature_deg:.0f}°  ·  "
                   f"apical Ø {sp.apical_diameter:.2f} mm  ·  source: {sp.source}")
            while self.f_small.size(det)[0] > W - 200:
                det = det[:-4] + "…"
            self.s.blit(self.f_small.render(det, True, MUTED), (100, y + 36))
            y += 74
        self.s.blit(self.f.render("↑/↓ or 1–6 to select  ·  ENTER to start  ·  ESC to quit", True, AR_AMBER), (82, H - 60))

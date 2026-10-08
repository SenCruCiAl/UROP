"""Augmented-reality layer built on Azuma's (1997) three AR properties:

 1. Combines real and virtual   -> the 'real' radiographic tooth view is
                                   overlaid with virtual guidance graphics.
 2. Interactive in real time    -> overlays + prompts update every frame from
                                   the user's inputs (latency is measured).
 3. Registered in 3D            -> every overlay is anchored in the tooth
                                   coordinate frame and drawn through a tracked
                                   pose; tracking error/latency can be injected
                                   (R) to show what mis-registration looks like.
"""
import math
import random
from collections import deque

from twin import ACCESS, WORKING_LENGTH, SHAPING, OBTURATION, COMPLETE, FAILED


class Pose:
    """2D rigid pose of the tooth frame on screen (translation only, px)."""

    def __init__(self, x, y, scale):
        self.x, self.y, self.scale = x, y, scale

    def to_screen(self, pt):
        return (self.x + pt[0] * self.scale, self.y + pt[1] * self.scale)


class Tracker:
    """Simulates patient motion (the real pose) and an optical tracker (the estimate)."""

    def __init__(self, origin, scale):
        self.origin, self.scale = origin, scale
        self.patient_motion = False
        self.degraded = False          # inject latency + bias + jitter
        self.latency_s = 0.25
        self.bias_mm = (1.1, -0.6)
        self.t = 0.0
        self.history = deque()
        self.rng = random.Random(3)
        self.true_pose = Pose(*origin, scale)
        self.est_pose = Pose(*origin, scale)

    def update(self, dt):
        self.t += dt
        ox, oy = self.origin
        if self.patient_motion:
            ox += 9 * math.sin(self.t * 1.3) + 3 * math.sin(self.t * 3.7)
            oy += 5 * math.sin(self.t * 0.9 + 1)
        self.true_pose = Pose(ox, oy, self.scale)
        self.history.append((self.t, ox, oy))
        while self.history and self.history[0][0] < self.t - 1.0:
            self.history.popleft()
        if self.degraded:
            target = self.t - self.latency_s
            hx, hy = ox, oy
            for ht, x, y in self.history:
                if ht >= target:
                    hx, hy = x, y
                    break
            hx += self.bias_mm[0] * self.scale + self.rng.gauss(0, 1.5)
            hy += self.bias_mm[1] * self.scale + self.rng.gauss(0, 1.5)
            self.est_pose = Pose(hx, hy, self.scale)
        else:
            self.est_pose = Pose(ox, oy, self.scale)

    def registration_error_mm(self):
        dx = self.est_pose.x - self.true_pose.x
        dy = self.est_pose.y - self.true_pose.y
        return math.hypot(dx, dy) / self.scale


class Prompt:
    def __init__(self, priority, novice, expert, anchor=None, level=0):
        self.priority, self.novice, self.expert = priority, novice, expert
        self.anchor, self.level = anchor, level  # anchor in tooth-frame mm; level 0 info,1 warn,2 danger


class PromptEngine:
    """Adaptive AR prompts: content depends on stage, live twin state, the
    user's expertise setting, their error history and AR registration quality."""

    def __init__(self):
        self.expert = False
        self.current = None
        self.hold = 0.0

    def detail_level(self, twin):
        # Adaptive: an 'expert' who keeps making errors is switched back to guided prompts
        return "novice" if (not self.expert or twin.errors >= 3) else "expert"

    def candidates(self, twin, reg_err):
        p, out = twin.p, []
        tip = (p.centerline_x(twin.depth), twin.depth)
        s = twin.stage

        if reg_err > 0.5:
            out.append(Prompt(95, f"AR registration error {reg_err:.1f} mm — overlays are not trustworthy. "
                                  "Press R to restore tracking.",
                              f"Reg. err {reg_err:.1f} mm", None, 2))
        if s == FAILED:
            out.append(Prompt(100, f"FAILED: {twin.fail_reason}. Press F5 to retry.", "FAILED — F5", tip, 2))
            return out
        if s == COMPLETE:
            out.append(Prompt(100, f"Procedure complete. Score {twin.score()}/100. Press F5 for a new patient.",
                              f"Done {twin.score()}/100", None, 0))
            return out

        if s == ACCESS:
            if twin.depth < p.chamber_roof:
                out.append(Prompt(10, f"Drill (DOWN / wheel) through enamel & dentin. Pulp roof in "
                                      f"{p.chamber_roof - twin.depth:.1f} mm.",
                                  f"Roof −{p.chamber_roof - twin.depth:.1f}", (0, p.chamber_roof)))
            else:
                out.append(Prompt(20, "Pulp chamber reached. Stop! Press ENTER to begin working length.",
                                  "Chamber OK - ENTER", (0, p.chamber_roof)))
            if twin.depth > p.chamber_floor - 0.6:
                out.append(Prompt(90, "DANGER: bur is at the chamber floor — withdraw (UP) now!",
                                  "FLOOR!", (0, p.chamber_floor), 2))

        elif s == WORKING_LENGTH:
            loc = twin.apex_locator()
            if loc is None:
                out.append(Prompt(10, "Advance the #10 K-file into the canal orifice.", "Enter canal",
                                  (0, p.chamber_floor)))
            elif loc > 2:
                out.append(Prompt(10, f"Keep advancing. Apex locator: {loc:.1f} mm. Hold SHIFT for fine control near apex.",
                                  f"EAL {loc:.1f}", tip))
            elif loc > 0.3:
                out.append(Prompt(40, f"Approaching constriction ({loc:.1f} mm). Target reading 0.5 → press W to record.",
                                  f"EAL {loc:.1f} — W", tip, 1))
            elif loc >= 0:
                out.append(Prompt(50, "At apical constriction. Press W to record working length, then ENTER.",
                                  "Record W", tip, 0))
            else:
                out.append(Prompt(80, "Past the apex! Withdraw the file (UP).", "Over apex!", tip, 2))
            if twin.recorded_wl is not None:
                out.append(Prompt(30, f"WL recorded {twin.recorded_wl:.2f} mm. ENTER to start shaping (or re-record).",
                                  "ENTER", (0, twin.recorded_wl)))

        elif s == SHAPING:
            wl = twin.wl
            if twin.stress > 70:
                out.append(Prompt(85, f"File stress {twin.stress:.0f}% — pause (release keys) or withdraw to avoid separation.",
                                  f"Stress {twin.stress:.0f}%", tip, 2))
            if twin.debris > 60:
                out.append(Prompt(70, f"Debris {twin.debris:.0f}% — irrigate (hold I) with NaOCl.",
                                  "Irrigate", tip, 1))
            if twin.depth > wl + 0.3:
                out.append(Prompt(80, "Beyond working length — withdraw.", "> WL", tip, 2))
            if twin.max_depth_this_file >= wl - 0.5:
                if not twin.irrigated_since_file:
                    out.append(Prompt(35, f"#{twin.file_size} reached WL. Irrigate (I), then N for next file.",
                                      "I then N", tip, 0))
                else:
                    out.append(Prompt(30, f"#{twin.file_size} at WL. Press N for the next file.", "N", tip))
            else:
                if p.curvature_deg > 15 and twin.depth > p.curve_start - 1:
                    out.append(Prompt(25, f"Canal curves {p.curvature_deg:.0f}°. Use SHIFT (slow) and short strokes.",
                                      f"Curve {p.curvature_deg:.0f}°", (p.centerline_x(p.curve_start + 1), p.curve_start + 1), 1))
                out.append(Prompt(10, f"Advance ISO #{twin.file_size} to WL {wl:.1f} mm "
                                      f"({max(0.0, wl - twin.depth):.1f} mm to go).",
                                  f"#{twin.file_size} → {wl:.1f}", tip))

        elif s == OBTURATION:
            if twin.fill_level < 1:
                out.append(Prompt(10, f"Hold O to condense gutta-percha. Fill {twin.fill_level * 100:.0f}%.",
                                  f"Fill {twin.fill_level * 100:.0f}%", (p.centerline_x(p.target_wl), p.target_wl)))
            else:
                out.append(Prompt(20, "Canal sealed. Press ENTER to complete.", "ENTER", (0, p.chamber_floor)))
        return out

    def update(self, dt, twin, reg_err):
        c = sorted(self.candidates(twin, reg_err), key=lambda x: -x.priority)
        self.hold -= dt
        best = c[0] if c else None
        # Hysteresis: keep a prompt at least 0.6 s unless something more urgent appears
        if best and (self.current is None or self.hold <= 0 or best.priority > self.current.priority
                     or best.priority == self.current.priority):
            if self.current is None or best.novice != self.current.novice:
                self.hold = 0.6
            self.current = best
        return c

    def text(self, prompt, twin):
        return prompt.novice if self.detail_level(twin) == "novice" else prompt.expert

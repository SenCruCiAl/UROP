"""Digital Surgical Twin (DST) of a root canal procedure.

The twin mirrors the state of the tooth + instruments in real time and is
driven entirely by the user's inputs (see main.py). It also keeps a
time-series log so a session can be replayed / analysed afterwards.
"""
import csv
import json
import os
import random
import time

import numpy as np

from anatomy import PatientTooth, ROOT_DIR

ACCESS, WORKING_LENGTH, SHAPING, OBTURATION, COMPLETE, FAILED = (
    "Access opening", "Working length", "Cleaning & shaping", "Obturation", "Complete", "Failed")
STAGES = [ACCESS, WORKING_LENGTH, SHAPING, OBTURATION]

ISO_SEQUENCE = [15, 20, 25, 30, 35, 40]


class Event:
    def __init__(self, t, level, text):
        self.t, self.level, self.text = t, level, text


class DigitalTwin:
    def __init__(self, patient: PatientTooth):
        self.p = patient
        self.stage = ACCESS
        self.t = 0.0
        self.depth = 0.0              # instrument tip depth (mm from reference)
        self.max_depth_this_file = 0.0
        self.instrument = "Bur"
        self.file_size = 10
        self.maf = 30 if patient.curvature_deg > 20 else 40   # master apical file
        self.file_queue = [s for s in ISO_SEQUENCE if s <= self.maf]
        self.files_done = []

        self.access_depth = 0.0
        self.recorded_wl = None
        self.wl_error = None

        n = len(patient.depths)
        self.shaped_radius = patient.orig_radius.copy()
        self.cleaned = np.zeros(n)
        self.debris = 0.0
        self.stress = 0.0
        self.peak_stress = 0.0
        self.irrigating = False
        self.irrigated_since_file = True
        self.fill_level = 0.0            # 0..1 of obturation
        self.over_instrumentation_s = 0.0
        self.penalties = []
        self.errors = 0
        self.events = []
        self.log_rows = []
        self.fail_reason = None
        self.rng = random.Random(patient.seed + 7)
        self.apex_noise = 0.0
        self._log_timer = 0.0
        self.log(0, f"Session started: {patient.spec.label}, canal {patient.canal_length:.2f} mm")

    # ------------------------------------------------------------------
    def log(self, level, text):
        self.events.append(Event(self.t, level, text))
        if level >= 2:
            self.errors += 1

    def penalise(self, pts, reason):
        self.penalties.append((pts, reason))
        self.log(2, f"-{pts}: {reason}")

    @property
    def wl(self):
        return self.recorded_wl if self.recorded_wl is not None else self.p.target_wl

    @property
    def in_canal(self):
        return self.depth > self.p.chamber_floor

    def apex_locator(self):
        """Simulated electronic apex locator reading (mm to apex, 0 = APEX)."""
        if self.instrument == "Bur" or not self.in_canal:
            return None
        return self.p.apex - self.depth + self.apex_noise

    def file_tip_radius(self):
        return self.file_size / 200.0

    def cleanliness(self):
        p = self.p
        i0, i1 = p.index(p.chamber_floor), p.index(p.target_wl)
        return float(np.mean(self.cleaned[i0:i1])) if i1 > i0 else 0.0

    def score(self):
        s = 100 - sum(pts for pts, _ in self.penalties)
        c = self.cleanliness()
        if c < 0.85 and self.stage in (OBTURATION, COMPLETE):
            s -= int((0.85 - c) * 60)
        return max(0, s)

    # ------------------------------------------------------------------
    def move(self, delta, fine):
        """User moved the instrument by `delta` mm (positive = deeper)."""
        if self.stage in (COMPLETE, FAILED, OBTURATION):
            return
        p = self.p
        limit = p.apex + 2.0
        if self.stage == SHAPING and self.debris >= 85 and delta > 0:
            block = self.wl - 2.5
            if self.depth + delta > block:
                if self.depth < block:
                    self.log(1, "Debris blockage — irrigate to clear the canal")
                delta = max(0.0, block - self.depth)
        new = float(np.clip(self.depth + delta, 0.0, limit))
        moved = new - self.depth
        old = self.depth
        self.depth = new
        if moved == 0:
            return

        if self.stage == ACCESS:
            self.access_depth = max(self.access_depth, self.depth)
            if self.depth > p.chamber_floor + 0.4:
                self.fail("Pulp-chamber floor perforated by the bur")
            return

        # Files: cutting on the down-stroke raises instrument stress
        if moved > 0 and self.in_canal:
            curve = 1 + p.curvature_deg / 15 if self.depth > p.curve_start else 1
            speed = 1.0 if fine else 1.35
            size = self.file_size / 15
            self.stress += moved * size * curve * (1 + self.debris / 100) * speed * 1.6
            self.peak_stress = max(self.peak_stress, self.stress)
            if self.stress >= 100:
                self.fail(f"ISO {self.file_size} file separated in the canal (instrument fatigue)")
                return

        if self.stage == SHAPING and self.in_canal:
            self.max_depth_this_file = max(self.max_depth_this_file, self.depth)
            self._shape(old, new)
            self.debris = min(100.0, self.debris + abs(moved) * self.file_size / 40 * 2.2)

    def _shape(self, a, b):
        p = self.p
        lo, hi = sorted((a, b))
        tip = self.depth
        # Every point of the canal currently occupied by the file is enlarged to the file's taper
        i0, i1 = p.index(p.chamber_floor), p.index(min(tip, p.apex))
        for i in range(i0, i1 + 1):
            d = p.depths[i]
            r = self.file_tip_radius() + 0.01 * (tip - d)  # ISO 0.02 taper → radius 0.01/mm
            self.shaped_radius[i] = max(self.shaped_radius[i], min(r, 0.9))
        j0, j1 = p.index(max(lo, p.chamber_floor)), p.index(min(hi, p.apex))
        self.cleaned[j0:j1 + 1] = np.minimum(1.0, self.cleaned[j0:j1 + 1] + 0.06 * self.file_size / 15)

    # ------------------------------------------------------------------
    def record_wl(self):
        if self.stage != WORKING_LENGTH:
            return
        if not self.in_canal:
            self.log(1, "Insert the #10 file into the canal before recording WL")
            return
        self.recorded_wl = round(self.depth, 2)
        self.wl_error = self.recorded_wl - self.p.target_wl
        e = abs(self.wl_error)
        quality = "excellent" if e <= 0.5 else "acceptable" if e <= 1.0 else "poor"
        self.log(0 if e <= 1.0 else 2, f"WL recorded {self.recorded_wl:.2f} mm ({quality}, error {self.wl_error:+.2f})")

    def next_stage(self):
        p = self.p
        if self.stage == ACCESS:
            if self.access_depth < p.chamber_roof:
                self.log(1, "Pulp chamber not reached yet")
                return
            self.stage, self.instrument, self.file_size = WORKING_LENGTH, "K-file", 10
            self.depth = 0.0
            i0, i1 = p.index(p.chamber_roof), p.index(p.chamber_floor)
            self.cleaned[i0:i1 + 1] = 1.0  # coronal pulp removed after de-roofing
            self.log(0, "Access complete → working-length determination with ISO #10 K-file")
        elif self.stage == WORKING_LENGTH:
            if self.recorded_wl is None:
                self.log(1, "Record working length (W) first")
                return
            e = abs(self.wl_error)
            if e > 0.5:
                self.penalise(int(round((e - 0.5) * 12)), f"WL error {self.wl_error:+.2f} mm")
            self.stage = SHAPING
            self.file_size = self.file_queue.pop(0)
            self.depth, self.max_depth_this_file, self.stress = 0.0, 0.0, 0.0
            self.log(0, f"Shaping: ISO #{self.file_size} to WL {self.wl:.2f} mm (MAF #{self.maf})")
        elif self.stage == SHAPING:
            self.next_file()
        elif self.stage == OBTURATION:
            if self.fill_level < 1:
                self.log(1, "Canal not fully obturated")
                return
            self.stage = COMPLETE
            self.log(0, f"Procedure complete — score {self.score()}")

    def next_file(self):
        if self.stage != SHAPING:
            return
        if self.max_depth_this_file < self.wl - 0.5:
            self.log(1, f"#{self.file_size} has not reached WL ({self.max_depth_this_file:.1f}/{self.wl:.1f} mm)")
            return
        if not self.irrigated_since_file:
            self.penalise(4, f"No irrigation after #{self.file_size}")
        self.files_done.append(self.file_size)
        self.irrigated_since_file = False
        if self.file_queue:
            self.file_size = self.file_queue.pop(0)
            self.depth, self.max_depth_this_file, self.stress = 0.0, 0.0, 0.0
            self.log(0, f"Switched to ISO #{self.file_size}")
        else:
            if self.debris > 30:
                self.penalise(6, "Obturating a canal with residual debris (voids)")
            self.stage = OBTURATION
            self.instrument = "Gutta-percha"
            self.depth = 0.0
            self.log(0, "Shaping finished → obturation (hold O)")

    def fail(self, reason):
        self.stage = FAILED
        self.fail_reason = reason
        self.log(3, reason)

    # ------------------------------------------------------------------
    def update(self, dt, irrigating, filling, moving):
        self.t += dt
        self.irrigating = irrigating
        p = self.p
        if self.stage in (COMPLETE, FAILED):
            return
        if not moving:
            self.stress = max(0.0, self.stress - 18 * dt)
        self.apex_noise += (self.rng.gauss(0, 0.05) - self.apex_noise) * min(1, dt * 4)

        if irrigating and self.stage in (SHAPING, WORKING_LENGTH, OBTURATION):
            self.debris = max(0.0, self.debris - 45 * dt)
            self.irrigated_since_file = True
            shaped = self.shaped_radius > p.orig_radius + 0.02
            self.cleaned[shaped] = np.minimum(1.0, self.cleaned[shaped] + 0.25 * dt)

        if self.depth > p.apex + 0.05 and self.instrument != "Bur":
            self.over_instrumentation_s += dt
            if self.over_instrumentation_s > 1.0:
                self.penalise(5, "Instrument pushed beyond the apex (over-instrumentation)")
                self.over_instrumentation_s = -2.0  # cool-down before next penalty

        if self.stage == OBTURATION and filling:
            self.fill_level = min(1.0, self.fill_level + 0.14 * dt)
            if self.fill_level >= 1.0 and not any("obturated" in e.text for e in self.events):
                self.log(0, "Canal obturated — press ENTER to finish")

        self._log_timer += dt
        if self._log_timer >= 0.1:
            self._log_timer = 0.0
            loc = self.apex_locator()
            self.log_rows.append({
                "t_s": round(self.t, 2), "stage": self.stage, "instrument": self.instrument,
                "file_iso": self.file_size, "depth_mm": round(self.depth, 2),
                "apex_locator_mm": "" if loc is None else round(loc, 2),
                "stress_pct": round(self.stress, 1), "debris_pct": round(self.debris, 1),
                "cleanliness_pct": round(self.cleanliness() * 100, 1),
                "fill_pct": round(self.fill_level * 100, 1), "irrigating": int(irrigating),
            })

    # ------------------------------------------------------------------
    def save(self):
        out = os.path.join(ROOT_DIR, "outputs")
        os.makedirs(out, exist_ok=True)
        stamp = time.strftime("%Y%m%d_%H%M%S")
        csv_path = os.path.join(out, f"session_{stamp}.csv")
        if self.log_rows:
            with open(csv_path, "w", newline="") as f:
                w = csv.DictWriter(f, fieldnames=list(self.log_rows[0].keys()))
                w.writeheader()
                w.writerows(self.log_rows)
        summary = {
            "tooth": self.p.spec.label, "patient_seed": self.p.seed,
            "canal_length_mm": self.p.canal_length, "curvature_deg": round(self.p.curvature_deg, 1),
            "target_wl_mm": self.p.target_wl, "recorded_wl_mm": self.recorded_wl,
            "final_stage": self.stage, "fail_reason": self.fail_reason,
            "files_used": self.files_done, "cleanliness_pct": round(self.cleanliness() * 100, 1),
            "peak_stress_pct": round(self.peak_stress, 1), "score": self.score(),
            "penalties": [{"points": p, "reason": r} for p, r in self.penalties],
            "events": [{"t": round(e.t, 2), "level": e.level, "text": e.text} for e in self.events],
        }
        json_path = os.path.join(out, f"session_{stamp}_summary.json")
        with open(json_path, "w") as f:
            json.dump(summary, f, indent=2)
        return csv_path, json_path

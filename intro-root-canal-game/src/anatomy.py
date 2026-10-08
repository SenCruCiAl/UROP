"""Loads the tooth dataset and builds a patient-specific 2D tooth geometry.

All geometry is in millimetres in the *tooth frame*:
    x = mesio-distal (lateral) offset, y = depth from the occlusal/incisal reference point.
"""
import csv
import math
import os
import random
from dataclasses import dataclass

import numpy as np

ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_PATH = os.path.join(ROOT_DIR, "data", "root_canal_anatomy.csv")

SAMPLE_STEP = 0.1  # mm resolution of the canal model


@dataclass
class ToothSpec:
    id: int
    tooth: str
    canal: str
    mean_length: float
    sd_length: float
    crown_length: float
    crown_width: float
    curvature_deg: float
    curvature_sd: float
    apical_diameter: float
    source: str
    notes: str

    @property
    def label(self):
        return f"{self.tooth} ({self.canal} canal)"


def load_specs(path=DATA_PATH):
    specs = []
    with open(path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            specs.append(ToothSpec(
                id=int(row["id"]),
                tooth=row["tooth"],
                canal=row["canal"],
                mean_length=float(row["mean_length_mm"]),
                sd_length=float(row["sd_mm"]),
                crown_length=float(row["crown_length_mm"]),
                crown_width=float(row["crown_width_mm"]),
                curvature_deg=float(row["curvature_deg"]),
                curvature_sd=float(row["curvature_sd_deg"]),
                apical_diameter=float(row["apical_diameter_mm"]),
                source=row["length_source"],
                notes=row["notes"],
            ))
    return specs


class PatientTooth:
    """A single 'patient' drawn from the population statistics in the dataset."""

    def __init__(self, spec: ToothSpec, seed=None):
        self.spec = spec
        self.seed = seed if seed is not None else random.randrange(1_000_000)
        rng = random.Random(self.seed)
        lo, hi = spec.mean_length - 2 * spec.sd_length, spec.mean_length + 2 * spec.sd_length
        self.canal_length = round(min(hi, max(lo, rng.gauss(spec.mean_length, spec.sd_length))), 2)
        self.curvature_deg = max(0.0, rng.gauss(spec.curvature_deg, spec.curvature_sd))
        self.crown_length = spec.crown_length
        self.crown_width = spec.crown_width
        self.apical_diameter = spec.apical_diameter

        # Key landmarks (depth, mm)
        self.apex = self.canal_length                     # radiographic apex
        self.target_wl = round(self.apex - 0.5, 2)         # apical constriction ~0.5 mm short
        self.chamber_roof = self.crown_length * 0.55
        self.chamber_floor = self.crown_length + 1.5
        self.curve_start = self.chamber_floor + 0.55 * (self.apex - self.chamber_floor)

        # Sampled canal model
        self.depths = np.arange(0.0, self.apex + 2.5, SAMPLE_STEP)
        self.orig_radius = np.array([self._orig_radius(d) for d in self.depths])

    # ---- geometry -----------------------------------------------------
    def centerline_x(self, d):
        if d <= self.curve_start:
            return 0.0
        span = max(1.0, self.apex - self.curve_start)
        k = math.tan(math.radians(self.curvature_deg)) / (2 * span)
        return k * (d - self.curve_start) ** 2

    def _orig_radius(self, d):
        if d < self.chamber_roof or d > self.apex:
            return 0.0
        if d <= self.chamber_floor:
            return 1.7  # pulp chamber half-width
        t = (d - self.chamber_floor) / (self.apex - self.chamber_floor)
        return 0.55 * (1 - t) + (self.apical_diameter / 2) * t

    def half_width(self, d):
        """Outer half-width of the tooth at depth d."""
        cl, w = self.crown_length, self.crown_width
        root_cej = w * 0.34
        if d <= cl:
            t = d / cl
            if t < 0.5:
                return w / 2 * (0.78 + 0.22 * math.sin(math.pi * t))
            return w / 2 + (root_cej - w / 2) * ((t - 0.5) / 0.5) ** 1.4
        t = min(1.0, (d - cl) / (self.apex - cl))
        return root_cej * (1 - t) ** 0.8 + 0.9 * t if t < 1 else 0.9

    def outline(self, extra=0.0, step=0.4):
        """Tooth outline polygon (mm). `extra` inflates it (for PDL / enamel layers)."""
        left, right = [], []
        d = 0.0
        while d <= self.apex + 0.5:
            cx = self.centerline_x(d)
            hw = self.half_width(min(d, self.apex)) + extra
            if d > self.apex:
                hw *= max(0.05, 1 - (d - self.apex) / 0.5)
            left.append((cx - hw, d - extra))
            right.append((cx + hw, d - extra))
            d += step
        return left + right[::-1]

    def enamel_outline(self):
        """Enamel cap: the crown portion of the outline."""
        pts_l, pts_r = [], []
        d = 0.0
        while d <= self.crown_length:
            hw = self.half_width(d)
            pts_l.append((-hw, d))
            pts_r.append((hw, d))
            d += 0.25
        return pts_l + pts_r[::-1]

    def index(self, d):
        return int(np.clip(round(d / SAMPLE_STEP), 0, len(self.depths) - 1))

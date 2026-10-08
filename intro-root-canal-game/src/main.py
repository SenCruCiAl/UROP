"""SurgiFlow Twin — interactive root-canal Digital Surgical Twin with Azuma-style AR.

Run:   python src/main.py            (interactive)
       python src/main.py --selftest (autopilot run, saves screenshots to outputs/)
"""
import os
import sys
import time

import pygame

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from anatomy import load_specs, PatientTooth, ROOT_DIR  # noqa: E402
from ar import Tracker, PromptEngine  # noqa: E402
from renderer import Renderer, W, H, SCALE, BG  # noqa: E402
from twin import DigitalTwin, ACCESS, WORKING_LENGTH, SHAPING, OBTURATION, COMPLETE, FAILED  # noqa: E402

MOVE_SPEED = 4.0   # mm/s with arrow keys
FINE_SPEED = 1.0   # mm/s with SHIFT held


class App:
    def __init__(self, selftest=False):
        pygame.init()
        pygame.display.set_caption("SurgiFlow Twin — Root Canal Digital Surgical Twin")
        self.screen = pygame.display.set_mode((W, H))
        self.clock = pygame.time.Clock()
        self.r = Renderer(self.screen)
        self.specs = load_specs()
        self.sel = 0
        self.mode = "menu"
        self.selftest = selftest
        self.ar_on = True
        self.help = False
        self.twin = None
        self.saved = None
        self.expert = False

    def start(self, seed=None):
        patient = PatientTooth(self.specs[self.sel], seed)
        self.twin = DigitalTwin(patient)
        self.tracker = Tracker(self.r.base_origin, SCALE)
        self.engine = PromptEngine()
        self.engine.expert = self.expert
        self.r.build_base(patient)
        self.saved = None
        self.mode = "sim"

    def screenshot(self, name=None):
        out = os.path.join(ROOT_DIR, "outputs")
        os.makedirs(out, exist_ok=True)
        path = os.path.join(out, name or f"screenshot_{time.strftime('%Y%m%d_%H%M%S')}.png")
        pygame.image.save(self.screen, path)
        return path

    # ------------------------------------------------------------------
    def handle_event(self, e):
        if e.type == pygame.QUIT:
            return False
        if self.mode == "menu":
            if e.type == pygame.KEYDOWN:
                if e.key == pygame.K_ESCAPE:
                    return False
                if e.key == pygame.K_UP:
                    self.sel = (self.sel - 1) % len(self.specs)
                elif e.key == pygame.K_DOWN:
                    self.sel = (self.sel + 1) % len(self.specs)
                elif pygame.K_1 <= e.key <= pygame.K_9 and e.key - pygame.K_1 < len(self.specs):
                    self.sel = e.key - pygame.K_1
                elif e.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
                    self.start()
            elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 1:
                idx = (e.pos[1] - 200) // 74
                if 0 <= idx < len(self.specs):
                    self.sel = idx
                    self.start()
            return True

        t = self.twin
        if e.type == pygame.MOUSEWHEEL:
            fine = pygame.key.get_mods() & pygame.KMOD_SHIFT
            t.move(-e.y * (0.1 if fine else 0.5), bool(fine))
            self._moved = True
        if e.type != pygame.KEYDOWN:
            return True
        k = e.key
        if k == pygame.K_ESCAPE:
            if self.help:
                self.help = False
            else:
                self.finish()
                return False
        elif k == pygame.K_h:
            self.help = not self.help
        elif k in (pygame.K_RETURN, pygame.K_KP_ENTER):
            t.next_stage()
        elif k == pygame.K_w:
            t.record_wl()
        elif k == pygame.K_n:
            t.next_file()
        elif k == pygame.K_a:
            self.ar_on = not self.ar_on
        elif k == pygame.K_r:
            self.tracker.degraded = not self.tracker.degraded
            t.log(1 if self.tracker.degraded else 0,
                  "Tracker degraded (latency + bias)" if self.tracker.degraded else "Tracking restored")
        elif k == pygame.K_m:
            self.tracker.patient_motion = not self.tracker.patient_motion
        elif k == pygame.K_e:
            self.expert = self.engine.expert = not self.engine.expert
        elif k == pygame.K_p:
            t.log(0, f"Screenshot saved: {os.path.basename(self.screenshot())}")
        elif k == pygame.K_F5:
            self.finish()
            self.start()
        elif k == pygame.K_TAB:
            self.finish()
            self.mode = "menu"
        return True

    def finish(self):
        if self.twin and not self.saved and self.twin.log_rows:
            self.saved = self.twin.save()

    # ------------------------------------------------------------------
    def step(self, dt, keys=None, mods=None, irrigate=None, fill=None):
        if self.mode != "sim":
            self.r.draw_menu(self.specs, self.sel)
            return
        t = self.twin
        keys = keys if keys is not None else pygame.key.get_pressed()
        mods = mods if mods is not None else pygame.key.get_mods()
        fine = bool(mods & pygame.KMOD_SHIFT)
        speed = FINE_SPEED if fine else MOVE_SPEED
        moving = getattr(self, "_moved", False)
        self._moved = False
        if not self.help:
            if keys[pygame.K_DOWN]:
                t.move(speed * dt, fine)
                moving = True
            if keys[pygame.K_UP]:
                t.move(-speed * dt, fine)
                moving = True
        irrigate = keys[pygame.K_i] if irrigate is None else irrigate
        fill = keys[pygame.K_o] if fill is None else fill
        t.update(dt, irrigate, fill, moving)
        self.tracker.update(dt)
        err = self.tracker.registration_error_mm()
        self.engine.update(dt, t, err)
        if t.stage in (COMPLETE, FAILED):
            self.finish()

        # ---- draw
        self.screen.fill(BG)
        self.r.draw_real(t, self.tracker.true_pose)
        if self.ar_on:
            self.r.draw_ar(t, self.tracker.est_pose, self.tracker.true_pose, self.engine, err)
        self.r.draw_header(t)
        self.r.draw_footer()
        self.r.draw_dashboard(t, self.tracker, self.engine, self.clock.get_fps() or 60, self.ar_on)
        if t.stage in (COMPLETE, FAILED):
            self.r.draw_result(t, self.saved)
        if self.help:
            self.r.draw_help()

    def run(self):
        running = True
        while running:
            dt = min(0.05, self.clock.tick(60) / 1000)
            for e in pygame.event.get():
                if not self.handle_event(e):
                    running = False
            if running:
                self.step(dt)
                pygame.display.flip()
        pygame.quit()

    # ------------------------------------------------------------------
    def autopilot(self):
        """Scripted run through the whole procedure (used for testing / demo)."""
        self.sel = 5  # curved MB canal = hardest case
        self.start(seed=42)
        t, dt = self.twin, 1 / 60
        nokeys = pygame.key.ScancodeWrapper([False] * 512)
        shots = {}

        def frame(irr=False, fill=False):
            self.step(dt, keys=nokeys, mods=0, irrigate=irr, fill=fill)

        def goto(depth, fine=True, stress_cap=45):
            for _ in range(20000):
                if t.stage == FAILED or abs(t.depth - depth) < 0.05:
                    return
                if t.stress > stress_cap:
                    for _ in range(90):
                        frame()
                    continue
                step = (FINE_SPEED if fine else MOVE_SPEED) * dt
                t.move(max(-step, min(step, depth - t.depth)), fine)
                self._moved = True
                if t.debris > 70:
                    for _ in range(60):
                        frame(irr=True)
                frame()

        goto(t.p.chamber_roof + 0.3, fine=False)
        self.screenshot("selftest_1_access.png")
        t.next_stage()
        goto(t.p.target_wl - 1.5, fine=False)
        goto(t.p.target_wl)
        frame()
        self.screenshot("selftest_2_working_length.png")
        t.record_wl()
        t.next_stage()
        while t.stage == SHAPING:
            goto(t.wl)
            if t.file_size == 25 and "s" not in shots:
                shots["s"] = 1
                self.tracker.degraded = True
                self.tracker.patient_motion = True
                for _ in range(40):
                    frame()
                self.screenshot("selftest_3_shaping_misregistered.png")
                self.tracker.degraded = False
                self.tracker.patient_motion = False
            goto(t.p.chamber_floor - 1, fine=False)
            for _ in range(80):
                frame(irr=True)
            t.next_file()
            if t.stage == FAILED:
                break
        for _ in range(60 * 9):
            frame(fill=True)
            if t.fill_level >= 1:
                break
        frame()
        self.screenshot("selftest_4_obturation.png")
        t.next_stage()
        frame()
        self.screenshot("selftest_5_result.png")
        self.finish()
        print("stage:", t.stage, "| score:", t.score(), "| wl err:", t.wl_error,
              "| clean:", round(t.cleanliness(), 2), "| peak stress:", round(t.peak_stress, 1))
        for e in t.events:
            print(f"  {e.t:6.1f}s [{e.level}] {e.text}")
        pygame.quit()


if __name__ == "__main__":
    app = App(selftest="--selftest" in sys.argv)
    if app.selftest:
        app.autopilot()
    else:
        app.run()

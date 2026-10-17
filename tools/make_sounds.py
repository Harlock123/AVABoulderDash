#!/usr/bin/env python3
"""Synthesises AVABoulderDash's sound effects and tunes into ../src/AVABoulderDash/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVABoulderDash", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out



def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Digging, stepping, pushing.
    write("Dig.wav", tone(0.07, 900, 300, "noise", attack=0.002, release=1.4, gain=0.6), 0.2)
    write("Step.wav", tone(0.03, 400, 300, "noise", attack=0.002, release=2, gain=0.3), 0.12)
    write("Push.wav", mix(tone(0.12, 80, 60, "saw", attack=0.01, release=0.8, gain=0.5), tone(0.1, 500, 200, "noise", release=1.2, gain=0.3)), 0.24)
    # Things landing: a boulder's thud, a diamond's ping.
    write("Boulder.wav", mix(tone(0.14, 90, 40, "sine", attack=0.002, release=1.2), tone(0.08, 700, 150, "noise", release=1.5, gain=0.5)), 0.32)
    write("Diamond.wav", mix(tone(0.25, hz("E7"), kind="sine", release=1.0), tone(0.25, hz("B7"), kind="sine", release=1.4, gain=0.4)), 0.16)
    write("Collect.wav", cat(tone(0.05, hz("C6"), kind="pulse", release=0.4), tone(0.08, hz("G6"), kind="pulse", release=0.6)), 0.24)
    write("ExitOpen.wav", cat(*[tone(0.06, hz(n), kind="square", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6"]], tone(0.3, hz("C7"), kind="square", vibrato=0.01, release=0.6)), 0.26)
    # Blasts, the magic wall, the slime.
    write("Explode.wav", mix(tone(0.7, 3000, 200, "noise", attack=0.002, release=0.6), tone(0.6, 120, 35, "sine", release=0.8, gain=0.8)), 0.46)
    write("Magic.wav", tone(0.35, 1200, 2400, "sine", vibrato=0.05, vib_rate=30, release=0.5), 0.2)
    write("SlimeShut.wav", cat(*[tone(0.08, 300 + 120 * i, 200 + 120 * i, "tri", vibrato=0.08, vib_rate=20, release=0.4) for i in range(6)]), 0.3)
    # Coming in, the clock, losing and gaining.
    write("Appear.wav", cat(*[tone(0.05, 600 + 180 * i, kind="square", release=0.3, gain=0.6) for i in range(6)]), 0.22)
    write("Bonus.wav", tone(0.03, hz("A6"), kind="square", attack=0.001, release=0.8, gain=0.5), 0.14)
    write("TimeLow.wav", tone(0.06, hz("E6"), kind="pulse", attack=0.001, release=0.6), 0.22)
    write("Die.wav", cat(tone(0.15, 600, 120, "square", release=0.6), *[tone(0.12, hz(n), kind="tri", release=0.5) for n in ["G4", "E4", "C4"]]), 0.4)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "G6", "C7"]]), 0.3)
    write("CaveStart.wav", tune(160, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5)], [("C3", 1), ("G2", 1), ("C3", 1)], kind="pulse"), 0.34)
    write("CaveClear.wav", tune(170, [("G5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C6", 0.5), ("E6", 0.5), ("G6", 1.5)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.36)

    # The title tune (an original composition): a bouncing line in C over a walking bass, a whole number of bars so it loops.
    lead = [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("E5", 0.5), ("A5", 1), ("G5", 1),
            ("F5", 0.5), ("A5", 0.5), ("C6", 0.5), ("A5", 0.5), ("G5", 2),
            ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("B5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5),
            ("D5", 0.5), ("E5", 0.5), ("F5", 0.5), ("D5", 0.5), ("C5", 2),
            ("G5", 0.5), ("G5", 0.5), ("A5", 0.5), ("G5", 0.5), ("E6", 1), ("D6", 1),
            ("C6", 0.5), ("B5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C6", 2),
            ("A5", 0.5), ("C6", 0.5), ("B5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5),
            ("C5", 1), ("G4", 1), ("C5", 2)]
    walk = [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 1), ("F2", 1), ("C3", 1), ("G2", 1), ("B2", 1),
            ("C3", 1), ("E3", 1), ("A2", 1), ("F2", 1), ("G2", 1), ("G2", 1), ("C3", 2)]
    write("TitleTune.wav", cat(tune(150, lead, walk + walk, kind="pulse"), silence(0.4)), 0.38)
    write("GameOver.wav", tune(80, [("G4", 1), ("E4", 1), ("C4", 1), ("B3", 1), ("C4", 4)], [("C3", 2), ("A2", 2), ("G2", 2), ("C2", 2)], kind="tri"), 0.4)


if __name__ == "__main__":
    main()

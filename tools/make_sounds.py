#!/usr/bin/env python3
"""Synthesises AVARipOff's sound effects and tunes into ../src/AVARipOff/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVARipOff", "Sounds")
RNG = random.Random(1980)


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




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The tank's gun, and the raiders' shot: a thump, and a thin zap.
    write("Fire.wav", mix(tone(0.16, 900, 160, "square", release=0.7), tone(0.08, 2400, 600, "noise", release=0.8, gain=0.5)), 0.26)
    write("RaiderFire.wav", tone(0.14, 2400, 1100, "pulse", release=0.6), 0.2)
    # A raider hooking a canister: a metallic clank and a rising whine.
    write("Hook.wav", cat(tone(0.05, 1800, 1700, "square", release=1.2), tone(0.25, 300, 700, "tri", release=0.5)), 0.34)
    # A canister gone off the edge: a falling, mournful slide.
    write("Escape.wav", cat(*[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4"]], tone(0.35, 440, 160, "tri", release=0.6)), 0.4)
    write("RaiderBlast.wav", mix(tone(0.5, 1300, 70, "noise", release=0.45), tone(0.4, 380, 50, "square", release=0.45, gain=0.5)), 0.42)
    write("TankHit.wav", cat(mix(tone(0.9, 1200, 40, "noise", release=0.3), tone(0.8, 400, 30, "saw", release=0.3, gain=0.7)), *[tone(0.1, hz(n), kind="tri", release=0.4) for n in ["G4", "D4"]]), 0.5)
    write("Respawn.wav", tone(0.4, 200, 800, "tri", attack=0.02, release=0.4, vibrato=0.05, vib_rate=18), 0.3)
    # A canister dropped: a light ping.
    write("Drop.wav", cat(tone(0.06, hz("C6"), kind="pulse", release=0.4), tone(0.12, hz("G6"), kind="pulse", release=0.5)), 0.3)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["A4", "E5", "A5"]]), 0.3)
    write("WaveClear.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["A5", "C#6", "E6", "A6", "E6", "A6"]]), 0.36)

    # The alarm while a canister is in tow: a two-tone siren, exactly one second, so it loops cleanly.
    write("Music.wav", cat(tone(0.5, 660, 660, "square", attack=0.004, release=0.05), tone(0.5, 520, 520, "square", attack=0.004, release=0.05)), 0.1)

    # The title: a tense, stalking theme in A minor (an original composition).
    lead = [("A4", 0.5), ("C5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 1), ("B4", 1),
            ("A4", 0.5), ("B4", 0.5), ("C5", 0.5), ("E5", 0.5), ("G#4", 2),
            ("A4", 0.5), ("E5", 0.5), ("A5", 0.5), ("G5", 0.5), ("F5", 1), ("E5", 1),
            ("D5", 0.5), ("C5", 0.5), ("B4", 0.5), ("G#4", 0.5), ("A4", 2)]
    bass = ([("A2", 0.5), ("A3", 0.5)] * 4 + [("E2", 0.5), ("E3", 0.5)] * 4
            + [("F2", 0.5), ("F3", 0.5)] * 4 + [("E2", 0.5), ("E3", 0.5)] * 2 + [("A2", 2)])
    write("TitleTune.wav", cat(tune(132, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(160, [("A4", 0.5), ("C5", 0.5), ("E5", 0.5), ("A5", 0.5), ("G#5", 0.5), ("A5", 1.5)], [("A2", 1), ("E2", 1), ("A2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("B4", 1), ("G#4", 1), ("A4", 4)], [("A2", 2), ("F2", 2), ("E2", 2), ("A1", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()

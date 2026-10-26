"""Generate a deterministic red cyber startup cue with no external samples."""
from pathlib import Path
import wave

import numpy as np

RATE = 44100
DURATION = 14.4
t = np.arange(int(RATE * DURATION), dtype=np.float64) / RATE
audio = np.zeros_like(t)


def tone(start: float, duration: float, frequency: float, gain: float) -> None:
    local = t - start
    active = (local >= 0) & (local < duration)
    x = local[active]
    envelope = np.minimum(x / 0.035, 1.0) * np.minimum((duration - x) / 0.46, 1.0)
    waves = (np.sin(2 * np.pi * frequency * x)
             + 0.24 * np.sin(2 * np.pi * frequency * 2 * x)
             + 0.1 * np.sin(2 * np.pi * frequency * 3 * x))
    audio[active] += gain * envelope * waves


def hit(start: float, gain: float = 0.09) -> None:
    local = t - start
    active = (local >= 0) & (local < 0.25)
    x = local[active]
    descending = 112 * x + 60 * (1 - np.exp(-24 * x))
    audio[active] += gain * np.sin(2 * np.pi * descending) * np.exp(-19 * x)


for start, frequency in [(0.3, 92.5), (1.55, 138.59), (2.75, 185.0),
                         (5.25, 92.5), (6.45, 146.83), (7.7, 185.0),
                         (10.35, 138.59), (11.5, 185.0), (12.45, 277.18)]:
    tone(start, 2.2, frequency, 0.044)
for start in [0.12, 0.65, 1.15, 1.65, 2.15, 2.65, 3.15, 3.65, 4.15,
              5.22, 5.72, 6.22, 6.72, 7.22, 7.72, 8.22, 8.72, 9.22,
              10.34, 11.2, 12.46, 13.18]:
    hit(start)
tone(12.55, 1.85, 369.99, 0.018)

audio = np.clip(audio, -0.78, 0.78)
target = Path(__file__).parent / "assets" / "boot.wav"
target.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(target), "wb") as wav:
    wav.setnchannels(1)
    wav.setsampwidth(2)
    wav.setframerate(RATE)
    wav.writeframes((audio * 32767).astype("<i2").tobytes())
print(target)

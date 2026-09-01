"""
Convert a speech transcript into a REFLACX-style word-level CSV:

    word,timestamp_start_word,timestamp_end_word

Two modes:
  A) --audio given  -> real word timestamps from faster-whisper
  B) text only      -> tokenised CSV with estimated (uniform) timings

Usage:
    python transcript_to_csv.py --text User6.txt --out out.csv
    python transcript_to_csv.py --audio User6.wav --out out.csv --model small
"""

import argparse
import csv
import re

# ----------------------------------------------------------------------
# 1. Cleaning
# ----------------------------------------------------------------------

BOILERPLATE = [
    r"\(Transcribed by TurboScribe.*?\)",
    r"\(This file is longer than.*?\)",
]

# filler / dictation-control chatter you may want dropped
FILLERS = {"okay", "ok", "let's see", "hmm", "right"}


def clean_text(text: str, drop_fillers: bool = False) -> str:
    for pat in BOILERPLATE:
        text = re.sub(pat, " ", text, flags=re.S | re.I)

    text = text.replace("\n", " ")
    text = re.sub(r"\s+", " ", text).strip()

    if drop_fillers:
        # collapse long runs of "Okay. Okay. Okay."
        text = re.sub(r"\b(okay|ok)\b[\s.,]*", " ", text, flags=re.I)
        text = re.sub(r"\blet's see\b[\s.,]*", " ", text, flags=re.I)
        text = re.sub(r"\s+", " ", text).strip()

    return text


# ----------------------------------------------------------------------
# 2. Tokenisation  (punctuation becomes its own token, as in REFLACX)
# ----------------------------------------------------------------------

TOKEN_RE = re.compile(r"[A-Za-zÀ-ÿ0-9]+(?:['’-][A-Za-zÀ-ÿ0-9]+)*|[.,;:?!]")


def tokenize(text: str, lowercase: bool = True) -> list[str]:
    toks = TOKEN_RE.findall(text)
    return [t.lower() for t in toks] if lowercase else toks


# ----------------------------------------------------------------------
# 3a. Timestamps from audio (real)
# ----------------------------------------------------------------------

def rows_from_audio(audio_path: str, model_size: str = "small",
                    language: str | None = None) -> list[tuple[str, float, float]]:
    from faster_whisper import WhisperModel  # pip install faster-whisper

    model = WhisperModel(model_size, device="cpu", compute_type="int8")
    segments, _ = model.transcribe(
        audio_path, word_timestamps=True, language=language, vad_filter=True
    )

    rows = []
    for seg in segments:
        for w in seg.words:
            raw = w.word.strip()
            if not raw:
                continue
            # split trailing punctuation into its own row, sharing the tail time
            m = re.match(r"^(.*?)([.,;:?!]+)$", raw)
            if m and m.group(1):
                word, punct = m.group(1), m.group(2)
                split = w.start + 0.85 * (w.end - w.start)
                rows.append((word.lower(), round(w.start, 2), round(split, 2)))
                for p in punct:
                    rows.append((p, round(split, 2), round(w.end, 2)))
            else:
                rows.append((raw.lower(), round(w.start, 2), round(w.end, 2)))
    return rows


# ----------------------------------------------------------------------
# 3b. Estimated timestamps (text only) -- NOT real timing
# ----------------------------------------------------------------------

def rows_from_text(tokens: list[str], start: float = 0.0,
                   wps: float = 2.5, pause: float = 0.45
                   ) -> list[tuple[str, float, float]]:
    """Uniform model: each word takes 1/wps seconds, punctuation adds a pause."""
    rows, t = [], start
    step = 1.0 / wps
    for tok in tokens:
        dur = pause if tok in ".,;:?!" else step
        rows.append((tok, round(t, 2), round(t + dur, 2)))
        t += dur
    return rows


# ----------------------------------------------------------------------
# 4. Writer
# ----------------------------------------------------------------------

def write_csv(rows, out_path: str) -> None:
    with open(out_path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["word", "timestamp_start_word", "timestamp_end_word"])
        w.writerows(rows)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--text", help="transcript .txt file")
    ap.add_argument("--audio", help="audio file (gives REAL timestamps)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--model", default="small")
    ap.add_argument("--language", default=None, help="e.g. pt or en")
    ap.add_argument("--drop-fillers", action="store_true")
    ap.add_argument("--wps", type=float, default=2.5)
    args = ap.parse_args()

    if args.audio:
        rows = rows_from_audio(args.audio, args.model, args.language)
    elif args.text:
        raw = open(args.text, encoding="utf-8").read()
        tokens = tokenize(clean_text(raw, args.drop_fillers))
        rows = rows_from_text(tokens, wps=args.wps)
        print("WARNING: timestamps are ESTIMATED, not measured.")
    else:
        ap.error("give --text or --audio")

    write_csv(rows, args.out)
    print(f"{len(rows)} rows -> {args.out}")


if __name__ == "__main__":
    main()
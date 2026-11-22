"""Loopback-only speech service using Chatterbox's built-in multilingual voice."""

import argparse
import io
import json
import os
from http.server import BaseHTTPRequestHandler, HTTPServer

import soundfile
import torch
from chatterbox.mtl_tts import ChatterboxMultilingualTTS


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8174)
    args = parser.parse_args()

    device = "cuda" if torch.cuda.is_available() else "cpu"
    torch.set_num_threads(min(6, os.cpu_count() or 1))
    print(f"Loading Chatterbox on {device}...", flush=True)
    model = ChatterboxMultilingualTTS.from_pretrained(device=device)

    class SpeechHandler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path != "/health" or self.headers.get("Host") != f"127.0.0.1:{args.port}":
                self.send_error(404)
                return
            self.send_response(204)
            self.end_headers()

        def do_POST(self):
            if self.path != "/speech" or self.headers.get("Host") != f"127.0.0.1:{args.port}":
                self.send_error(404)
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
                if not 0 < length <= 4096 or self.headers.get_content_type() != "application/json":
                    raise ValueError("Invalid request size or type")
                request = json.loads(self.rfile.read(length))
                text = request["text"]
                language = request["language"]
                if not isinstance(text, str) or not text.strip() or len(text) > 200 or language not in ("ar", "en"):
                    raise ValueError("Invalid speech input")
            except (KeyError, TypeError, ValueError):
                self.send_error(400, "Invalid speech input")
                return

            try:
                with torch.inference_mode():
                    wav = model.generate(text, language_id=language)
                clip = io.BytesIO()
                soundfile.write(clip, wav.squeeze(0).cpu().numpy(), model.sr, format="WAV")
                audio = clip.getvalue()
            except Exception as error:
                print(f"Speech synthesis failed: {type(error).__name__}", flush=True)
                self.send_error(503, "Speech synthesis failed")
                return
            try:
                self.send_response(200)
                self.send_header("Content-Type", "audio/wav")
                self.send_header("Content-Length", str(len(audio)))
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                self.wfile.write(audio)
            except (BrokenPipeError, ConnectionResetError):
                pass

    with HTTPServer(("127.0.0.1", args.port), SpeechHandler) as server:
        print(f"Speech ready on 127.0.0.1:{args.port}.", flush=True)
        server.serve_forever()


if __name__ == "__main__":
    main()

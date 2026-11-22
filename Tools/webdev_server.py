"""Serves a WebGL build locally and writes what the page reports to a log file.

    python Tools/webdev_server.py [build folder] [port]

Defaults to Builds/WebGL-Dev on port 8765. The page (Assets/WebGLTemplates/FullWindow, on localhost) POSTs its log
lines to /log, and they're appended to <build folder>-log.txt with the time they arrived. Each page load starts with
a "page start" line, so the newest session is everything after the last one.
"""
import os
import sys
import time
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else "Builds/WebGL-Dev")
port = int(sys.argv[2]) if len(sys.argv) > 2 else 8765
log_path = root.rstrip("\\/") + "-log.txt"


class Handler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map, ".wasm": "application/wasm", ".js": "application/javascript"}

    def do_POST(self):
        if self.path != "/log":
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length).decode("utf-8", "replace")
        stamp = time.strftime("%H:%M:%S")
        with open(log_path, "a", encoding="utf-8") as f:
            for line in body.splitlines():
                if line:
                    f.write(f"{stamp} {line}\n")
        self.send_response(204)
        self.end_headers()

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, *args):
        pass


print(f"Serving {root} on http://127.0.0.1:{port}/, logging to {log_path}", flush=True)
ThreadingHTTPServer(("127.0.0.1", port), partial(Handler, directory=root)).serve_forever()

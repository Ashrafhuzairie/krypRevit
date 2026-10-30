"""Contract fixture, NOT a Revit simulator. Never modifies a Revit model.
Run only with Revit closed. Uses the same local port as the real bridge.
"""
import json
import uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from urllib.parse import urlparse, parse_qs

HOSTS = [
    {"id": "1001", "unique_id": "mock-wall", "kind": "wall", "name": "Mock Wall",
     "type_name": "Generic 200mm", "level": "Level 1", "bbox_min_ft": [0, 0, 0], "bbox_max_ft": [30, 1, 12]},
    {"id": "1002", "unique_id": "mock-floor", "kind": "floor", "name": "Mock Floor",
     "type_name": "Concrete 200mm", "level": "Level 1", "bbox_min_ft": [0, 0, -0.67], "bbox_max_ft": [30, 40, 0]},
]


class H(BaseHTTPRequestHandler):
    def _send(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def allowed(self):
        if self.headers.get("Origin") or self.headers.get("X-krypRevit-Client") != "mcp":
            self._send(403, {"ok": False, "error": "Client header required; browsers rejected."})
            return False
        return True

    def do_GET(self):
        if not self.allowed():
            return
        u = urlparse(self.path)
        q = parse_qs(u.query)
        if u.path == "/health":
            return self._send(200, {"ok": True, "mock": True, "revit_version": "2024", "document": "MOCK.rvt", "is_family_doc": False})
        if u.path == "/hosts":
            kind = q.get("type", ["all"])[0]
            if kind not in ("all", "wall", "floor"):
                return self._send(400, {"ok": False, "error": "Invalid host type."})
            rows = [h for h in HOSTS if kind == "all" or h["kind"] == kind]
            return self._send(200, {"ok": True, "hosts": rows, "count": len(rows)})
        if u.path == "/model":
            uid = q.get("unique_id", [None])[0]
            if uid:
                element = next((h for h in HOSTS if h["unique_id"] == uid), None)
                if not element:
                    return self._send(400, {"ok": False, "error": "Element no longer exists."})
                return self._send(200, {"ok": True, "element": element, "parameters": [{"parameter_id": "-1010106", "name": "Comments", "storage": "String", "value": "", "read_only": False}]})
            return self._send(200, {"ok": True, "elements": HOSTS, "levels": [{"unique_id": "mock-level", "name": "Level 1", "elevation_ft": 0}], "wall_types": [{"unique_id": "mock-type", "name": "Basic wall"}]})
        if u.path == "/plans/status":
            return self._send(200, self.server.plan)
        self._send(404, {"ok": False, "error": "Route not found."})

    def do_POST(self):
        if not self.allowed():
            return
        if self.path != "/plans":
            return self._send(404, {"ok": False, "error": "Route not found. No HTTP apply endpoint exists."})
        if self.server.plan["state"] == "pending_review":
            return self._send(500, {"ok": False, "error": "A plan awaits review."})
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size <= 65536:
                raise ValueError()
            actions = json.loads(self.rfile.read(size))["actions"]
            if not isinstance(actions, list) or not 1 <= len(actions) <= 10:
                raise ValueError()
        except (ValueError, KeyError, TypeError):
            return self._send(400, {"ok": False, "error": "Invalid proposal."})
        self.server.plan = {"ok": True, "mock": True, "plan_id": uuid.uuid4().hex, "state": "pending_review",
                            "summary": actions, "next_step": "Mock cannot approve or execute actions. Use Revit for real validation."}
        self._send(200, self.server.plan)

    def log_message(self, *args):
        pass


def make_server(port=8765):
    server = HTTPServer(("127.0.0.1", port), H)
    server.plan = {"ok": True, "state": "none", "mock": True}
    return server


if __name__ == "__main__":
    make_server().serve_forever()

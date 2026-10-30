"""Tests the production C# TCP/HTTP listener, with Revit dispatch/endpoints substituted.
Set KRYPREVIT_DOTNET to a .NET 8 SDK dotnet executable to run these optional tests.
They do not test Revit behavior or execute model edits.
"""
import json
import os
from pathlib import Path
import socket
import subprocess
import unittest
import httpx

ROOT = Path(__file__).resolve().parents[1]
DOTNET = os.environ.get("KRYPREVIT_DOTNET")


@unittest.skipUnless(DOTNET, "Set KRYPREVIT_DOTNET to run C# transport tests")
class NativeTransport(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        project = ROOT / "tests/TransportHarness/TransportHarness.csproj"
        subprocess.run([DOTNET, "build", str(project), "-c", "Release", "--nologo", "-v:q"], check=True, capture_output=True, text=True)
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            cls.port = sock.getsockname()[1]
        assembly = project.parent / "bin/Release/net8.0/TransportHarness.dll"
        cls.process = subprocess.Popen([DOTNET, str(assembly), str(cls.port)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        if cls.process.stdout.readline().strip() != "READY":
            cls.process.kill()
            raise RuntimeError("C# transport did not start")
        cls.url = f"http://127.0.0.1:{cls.port}"
        cls.client = httpx.Client(base_url=cls.url, headers={"X-krypRevit-Client": "mcp"}, trust_env=False, timeout=5)

    @classmethod
    def tearDownClass(cls):
        cls.client.close()
        cls.process.communicate("\n", timeout=5)
        if cls.process.returncode != 0:
            raise RuntimeError("C# transport failed to stop cleanly")

    def raw(self, request):
        with socket.create_connection(("127.0.0.1", self.port), timeout=5) as sock:
            sock.sendall(request)
            chunks = []
            while True:
                block = sock.recv(65536)
                if not block:
                    break
                chunks.append(block)
        return b"".join(chunks)

    def test_health(self):
        self.assertTrue(self.client.get("/health").json()["transport_harness"])

    def test_browser_rejected(self):
        self.assertEqual(self.client.get("/health", headers={"Origin": "https://example.com"}).status_code, 403)

    def test_marker_required(self):
        with httpx.Client(trust_env=False) as c:
            self.assertEqual(c.get(self.url + "/health").status_code, 403)

    def test_unicode_json_body_roundtrip(self):
        payload = {"actions": [{"operation": "set_parameter", "value": "レビュー ✓"}]}
        r = self.client.post("/plans", json=payload)
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json()["echo"], payload)

    def test_method_guard(self):
        self.assertEqual(self.client.get("/plans").status_code, 405)

    def test_content_type_guard(self):
        self.assertEqual(self.client.post("/plans", content="{}", headers={"Content-Type": "application/jsonp"}).status_code, 400)

    def test_query_decoding(self):
        r = self.client.get("/model", params={"unique_id": "abc + 日本語", "limit": 7})
        self.assertEqual(r.json()["uid"], "abc + 日本語")
        self.assertEqual(r.json()["limit"], 7)

    def test_chunked_rejected(self):
        r = self.raw(f"POST /plans HTTP/1.1\r\nHost: 127.0.0.1:{self.port}\r\nTransfer-Encoding: chunked\r\n\r\n".encode())
        self.assertTrue(r.startswith(b"HTTP/1.1 400 "))

    def test_duplicate_content_length_rejected(self):
        r = self.raw(f"POST /plans HTTP/1.1\r\nHost: 127.0.0.1:{self.port}\r\nContent-Length: 0\r\nContent-Length: 1\r\n\r\n".encode())
        self.assertTrue(r.startswith(b"HTTP/1.1 400 "))

    def test_oversized_body_rejected_before_reading(self):
        r = self.raw(f"POST /plans HTTP/1.1\r\nHost: 127.0.0.1:{self.port}\r\nContent-Length: 65537\r\n\r\n".encode())
        self.assertTrue(r.startswith(b"HTTP/1.1 400 "))

    def test_host_guard(self):
        self.assertEqual(self.client.get("/health", headers={"Host": "example.com"}).status_code, 400)

    def test_model_search_and_type_scope(self):
        r = self.client.get("/search", params={"query": "Desk", "category": "Furniture", "include_types": "true", "limit": 7})
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json()["query"], "Desk")
        self.assertTrue(r.json()["includeTypes"])

    def test_units_payload_roundtrip(self):
        payload = {"unit": "mm", "values": [1000, 2500]}
        r = self.client.post("/units", json=payload)
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.json()["echo"], payload)

    def test_units_is_post_only(self):
        self.assertEqual(self.client.get("/units").status_code, 405)

    def test_no_apply_route(self):
        self.assertEqual(self.client.get("/apply").status_code, 404)


if __name__ == "__main__":
    unittest.main()

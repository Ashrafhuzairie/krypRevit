import asyncio
import json
import os
from pathlib import Path
import sys
import threading
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "mcp_server"))
import httpx
import server
import mock_bridge
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


class ClientErrors(unittest.TestCase):
    def invoke(self, response=None, error=None):
        with patch.object(httpx.Client, "request", side_effect=error, return_value=response):
            return server.revit_health()

    def test_connection_failure(self):
        result = self.invoke(error=httpx.ConnectError("offline"))
        self.assertFalse(result["ok"])
        self.assertIn("Cannot reach", result["error"])

    def test_timeout_requires_status_check(self):
        result = self.invoke(error=httpx.ReadTimeout("busy"))
        self.assertIn("plan_status", result["error"])

    def test_http_error_preserved(self):
        result = self.invoke(httpx.Response(400, json={"ok": False, "error": "Invalid host type."}))
        self.assertEqual(result, {"ok": False, "error": "Invalid host type."})

    def test_invalid_json(self):
        self.assertFalse(self.invoke(httpx.Response(502, text="not JSON"))["ok"])

    def test_invalid_shape(self):
        self.assertFalse(self.invoke(httpx.Response(200, json=[]))["ok"])

    def test_empty_plan_rejected_without_http(self):
        with patch.object(httpx.Client, "request") as request:
            self.assertFalse(server.propose_actions([])["ok"])
            request.assert_not_called()

    def test_oversized_plan_rejected(self):
        self.assertFalse(server.propose_actions([{}] * 11)["ok"])

    def test_invalid_model_limit(self):
        self.assertFalse(server.inspect_model(501)["ok"])


class ProtocolIntegration(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.bridge = mock_bridge.make_server(0)
        cls.url = f"http://127.0.0.1:{cls.bridge.server_port}"
        cls.thread = threading.Thread(target=cls.bridge.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        cls.bridge.shutdown()
        cls.bridge.server_close()
        cls.thread.join()

    def test_browser_and_unmarked_requests_rejected(self):
        with httpx.Client(trust_env=False) as client:
            self.assertEqual(client.get(self.url + "/health").status_code, 403)
            self.assertEqual(client.get(self.url + "/health", headers={"X-krypRevit-Client": "mcp", "Origin": "http://example.com"}).status_code, 403)

    def test_no_http_apply_endpoint(self):
        with httpx.Client(trust_env=False) as client:
            self.assertEqual(client.post(self.url + "/apply", headers={"X-krypRevit-Client": "mcp"}).status_code, 404)

    def test_full_mcp_stdio_roundtrip(self):
        asyncio.run(self.roundtrip())

    async def roundtrip(self):
        params = StdioServerParameters(command=sys.executable, args=[str(ROOT / "mcp_server" / "server.py")],
                                       env={**os.environ, "KRYPREVIT_BRIDGE": self.url})
        async with stdio_client(params) as (read, write):
            async with ClientSession(read, write) as session:
                await session.initialize()
                names = {t.name for t in (await session.list_tools()).tools}
                self.assertEqual(names, {"revit_health", "list_hosts", "inspect_model", "inspect_element", "propose_actions", "plan_status", "find_elements", "convert_units"})

                async def call(name, args=None):
                    result = await session.call_tool(name, args or {})
                    self.assertFalse(result.isError)
                    return json.loads(next(c.text for c in result.content if c.type == "text"))

                health = await call("revit_health")
                self.assertTrue(health["mock"])
                self.assertEqual(health["revit_version"], "2024")
                hosts = await call("list_hosts", {"kind": "wall"})
                self.assertEqual(hosts["count"], 1)
                model = await call("inspect_model")
                self.assertTrue(model["levels"])
                details = await call("inspect_element", {"unique_id": "mock-wall"})
                self.assertEqual(details["parameters"][0]["storage"], "String")
                self.assertFalse((await call("inspect_element", {"unique_id": "missing"}))["ok"])
                self.assertEqual((await call("plan_status"))["state"], "none")
                actions = [{"operation": "move", "unique_id": "mock-wall", "vector_ft": [1, 0, 0]}]
                proposed = await call("propose_actions", {"actions": actions})
                self.assertEqual(proposed["state"], "pending_review")
                # A second request must not overwrite an awaiting proposal.
                self.assertFalse((await call("propose_actions", {"actions": actions}))["ok"])
                status = await call("plan_status")
                self.assertEqual(status["plan_id"], proposed["plan_id"])
                self.assertEqual(status["state"], "pending_review")
                self.assertEqual((await call("list_hosts"))["hosts"], mock_bridge.HOSTS)


if __name__ == "__main__":
    unittest.main()

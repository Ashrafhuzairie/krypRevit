"""krypRevit MCP tools. Model writes require approval using the Revit ribbon."""
import os
from typing import Literal
from urllib.parse import urlsplit
import httpx
from mcp.server.fastmcp import FastMCP

BRIDGE_URL = os.environ.get("KRYPREVIT_BRIDGE", "http://127.0.0.1:5060").rstrip("/")
_url = urlsplit(BRIDGE_URL)
if (_url.scheme != "http" or _url.hostname != "127.0.0.1" or _url.username
        or _url.password or _url.path or _url.query or _url.fragment):
    raise ValueError("KRYPREVIT_BRIDGE must be an http://127.0.0.1:PORT URL")
TIMEOUT_S = float(os.environ.get("KRYPREVIT_TIMEOUT", "40"))
mcp = FastMCP("kryprevit")


def _request(method: str, path: str, *, params=None, payload=None) -> dict:
    try:
        # Localhost must not be routed through system/corporate HTTP proxies.
        with httpx.Client(timeout=TIMEOUT_S, trust_env=False) as client:
            response = client.request(method, f"{BRIDGE_URL}{path}", params=params,
                                      json=payload, headers={"X-krypRevit-Client": "mcp"})
        data = response.json()
        if not isinstance(data, dict):
            return {"ok": False, "error": "Bridge returned an invalid response."}
        if response.is_error:
            return {"ok": False, "error": data.get("error", f"HTTP {response.status_code}")}
        return data
    except httpx.TimeoutException:
        return {"ok": False, "error": "Bridge timed out. Query plan_status before retrying a proposal."}
    except httpx.RequestError:
        return {"ok": False, "error": "Cannot reach krypRevit. Open Revit 2024 and check krypRevit > MCP Status."}
    except ValueError:
        return {"ok": False, "error": "Bridge returned invalid JSON."}


@mcp.tool()
def revit_health() -> dict:
    """Read Revit version and active document. No changes."""
    return _request("GET", "/health")


@mcp.tool()
def list_hosts(kind: Literal["all", "wall", "floor"] = "all") -> dict:
    """Read walls/floors, unique IDs and bounding boxes in internal feet."""
    return _request("GET", "/hosts", params={"type": kind})


@mcp.tool()
def inspect_model(limit: int = 100) -> dict:
    """Read selected elements (or first model elements), levels and basic wall types.

    Use returned unique_id strings for actions; never invent IDs. Coordinates are feet.
    """
    if not 1 <= limit <= 500:
        return {"ok": False, "error": "limit must be 1–500"}
    return _request("GET", "/model", params={"limit": limit})


@mcp.tool()
def inspect_element(unique_id: str) -> dict:
    """Read an element's parameters, IDs, storage types, values, specs and read-only flags.

    Double values use Revit internal units (feet for length, radians for angle;
    other specs have other internal units). Do not infer units from the display string.
    """
    return _request("GET", "/model", params={"unique_id": unique_id})


@mcp.tool()
def find_elements(query: str = "", category: str = "", limit: int = 100, include_types: bool = False) -> dict:
    """Find project instances and actual geometry, or loaded types when include_types=True."""
    if not 1 <= limit <= 500:
        return {"ok": False, "error": "limit must be 1–500"}
    return _request("GET", "/search", params={"query": query, "category": category, "limit": limit, "include_types": include_types})


@mcp.tool()
def convert_units(values: list[float], unit: Literal["mm", "cm", "m", "ft", "in", "deg", "rad", "m2", "ft2", "m3", "ft3"]) -> dict:
    """Use native Revit units to convert 1–256 dimensions into the corresponding internal units.

    Use before modelling from metric/inch/degree prompts. Match the parameter's quantity spec.
    """
    if not 1 <= len(values) <= 256:
        return {"ok": False, "error": "Supply 1–256 numeric values."}
    return _request("POST", "/units", payload={"unit": unit, "values": values})


@mcp.tool()
def propose_actions(actions: list[dict]) -> dict:
    """Stage 1–10 model edits for explicit human review inside Revit. This DOES NOT APPLY edits.

    Operations (one action per existing element per plan):
    set_parameter: unique_id, parameter_id, value (string/integer/double, internal units).
    move or copy: unique_id, vector_ft [x,y,z].
    rotate: unique_id, origin_ft [x,y,z], axis [x,y,z], angle_radians.
    create_wall: wall_type_unique_id, level_unique_id, start_ft/end_ft [x,y,z], height_ft.
    create_floor: floor_type_unique_id, level_unique_id, boundary_ft [[x,y,z],...], simple horizontal polygon on level.
    create_level: name, elevation_ft (no plan views).
    place_family: symbol_unique_id, level_unique_id, point_ft [x,y,z]; loaded unhosted one-level nonstructural families only.
    change_type: unique_id, type_unique_id from inspect_element.valid_types; no type-definition edits.
    set_wall_curve: unique_id, start_ft/end_ft; straight basic walls, preserve base Z.
    Inspect catalogs/geometry and call convert_units before planning. New element IDs cannot be referenced in the same batch.
    Wall endpoints must have Z equal to the selected level elevation. Basic, nonstructural walls only.
    Inspect actual element/parameter/type IDs and units first; never invent them.
    No arbitrary code, deletion, type-definition edits, linked/pinned/grouped-element edits.
    Pending plans expire in ten minutes and become stale after a document change.
    Tell the user to click krypRevit > Review & apply in Revit, then query plan_status.
    Never claim success before state is applied. Do not retry an uncertain proposal without checking status.
    """
    if not 1 <= len(actions) <= 10:
        return {"ok": False, "error": "Supply 1–10 actions."}
    return _request("POST", "/plans", payload={"actions": actions})


@mcp.tool()
def plan_status() -> dict:
    """Read latest proposal state: pending_review, stale, cancelled, applied or failed.

    applied means the transaction committed; a later Revit Undo is not tracked here.
    No network tool can approve or apply a plan.
    """
    return _request("GET", "/plans/status")


if __name__ == "__main__":
    mcp.run()

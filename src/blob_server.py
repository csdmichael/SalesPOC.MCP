import base64
import os
from functools import lru_cache
from typing import Any

from azure.storage.blob import BlobServiceClient
from mcp.server.fastmcp import FastMCP

from policies import enforce_output_policy, enforce_rate_limit, enforce_text_policy

connection_string = os.getenv("AZURE_BLOB_CONNECTION_STRING")
container_name = os.getenv("AZURE_BLOB_CONTAINER_NAME", "semiconductor-product-documents")
host = os.getenv("MCP_HOST", "127.0.0.1")
port = int(os.getenv("MCP_PORT", os.getenv("PORT", os.getenv("WEBSITES_PORT", "8000"))))

if not connection_string:
    raise RuntimeError("Missing AZURE_BLOB_CONNECTION_STRING environment variable.")

mcp = FastMCP("azureblob-mcp-server", host=host, port=port)


@lru_cache(maxsize=1)
def get_container_client():
    service = BlobServiceClient.from_connection_string(connection_string)
    return service.get_container_client(container_name)


@mcp.tool()
def list_documents(prefix: str | None = None, limit: int = 50) -> dict[str, Any]:
    """List documents in the Azure Blob container."""
    enforce_rate_limit("blob.list_documents")
    enforce_text_policy(prefix, "prefix")

    container = get_container_client()
    safe_limit = max(1, min(int(limit), 200))

    blobs = []
    for blob in container.list_blobs(name_starts_with=(prefix or None)):
        blobs.append(
            {
                "name": blob.name,
                "size": blob.size,
                "content_type": blob.content_settings.content_type if blob.content_settings else None,
                "last_modified": blob.last_modified.isoformat() if blob.last_modified else None,
            }
        )
        if len(blobs) >= safe_limit:
            break

    return enforce_output_policy({"count": len(blobs), "items": blobs})


@mcp.tool()
def read_document(blob_name: str, max_chars: int = 20000) -> dict[str, Any]:
    """Read one blob document; returns text if decodable, else base64 content."""
    enforce_rate_limit("blob.read_document")
    enforce_text_policy(blob_name, "blob_name")

    container = get_container_client()

    name = (blob_name or "").strip()
    if not name:
        raise ValueError("'blob_name' is required.")

    safe_max = max(1, min(int(max_chars), 200000))
    client = container.get_blob_client(name)

    downloader = client.download_blob(max_concurrency=1)
    data = downloader.readall()

    properties = client.get_blob_properties()
    content_type = properties.content_settings.content_type if properties.content_settings else None

    try:
        text = data.decode("utf-8")
        return enforce_output_policy({
            "blob_name": name,
            "content_type": content_type,
            "encoding": "utf-8",
            "truncated": len(text) > safe_max,
            "content": text[:safe_max],
            "size_bytes": len(data),
        })
    except UnicodeDecodeError:
        encoded = base64.b64encode(data).decode("ascii")
        return enforce_output_policy({
            "blob_name": name,
            "content_type": content_type,
            "encoding": "base64",
            "truncated": len(encoded) > safe_max,
            "content": encoded[:safe_max],
            "size_bytes": len(data),
        })


if __name__ == "__main__":
    transport = os.getenv("MCP_TRANSPORT", "stdio").strip().lower()
    mount_path = os.getenv("MCP_MOUNT_PATH")

    if transport == "sse":
        mcp.run(transport="sse", mount_path=mount_path)
    elif transport in {"stdio", "streamable-http"}:
        mcp.run(transport=transport)
    else:
        raise RuntimeError(
            "Invalid MCP_TRANSPORT value. Expected one of: stdio, sse, streamable-http"
        )

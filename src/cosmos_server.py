import os
from functools import lru_cache
from typing import Any

from azure.cosmos import CosmosClient
from azure.identity import DefaultAzureCredential
from mcp.server.fastmcp import FastMCP

from policies import enforce_output_policy, enforce_rate_limit, enforce_text_policy

endpoint = os.getenv("COSMOS_ENDPOINT")
database_name = os.getenv("COSMOS_DATABASE_NAME", "sales")
container_name = os.getenv("COSMOS_CONTAINER_NAME", "products")
host = os.getenv("MCP_HOST", "127.0.0.1")
port = int(os.getenv("MCP_PORT", os.getenv("PORT", os.getenv("WEBSITES_PORT", "8000"))))

if not endpoint:
    raise RuntimeError("Missing COSMOS_ENDPOINT environment variable.")

credential = DefaultAzureCredential()

mcp = FastMCP("cosmos_server", host=host, port=port)


@lru_cache(maxsize=1)
def get_container():
    client = CosmosClient(endpoint, credential=credential)
    return client.get_database_client(database_name).get_container_client(container_name)


@mcp.tool()
def find_products(
    search_text: str | None = None,
    category: str | None = None,
    min_price: float | None = None,
    max_price: float | None = None,
    limit: int = 20,
) -> dict[str, Any]:
    """Find products by optional text/category/price filters."""
    enforce_rate_limit("cosmos.find_products")
    enforce_text_policy(search_text, "search_text")
    enforce_text_policy(category, "category")

    container = get_container()
    safe_limit = max(1, min(int(limit), 100))

    parameters: list[dict[str, Any]] = [{"name": "@limit", "value": safe_limit}]
    where_clauses: list[str] = []

    if search_text and search_text.strip():
        parameters.append({"name": "@search", "value": search_text.strip()})
        where_clauses.append(
            "(CONTAINS(c.name, @search, true) OR CONTAINS(c.description, @search, true))"
        )

    if category and category.strip():
        parameters.append({"name": "@category", "value": category.strip()})
        where_clauses.append("c.category = @category")

    if min_price is not None:
        parameters.append({"name": "@minPrice", "value": float(min_price)})
        where_clauses.append("IS_DEFINED(c.price) AND c.price >= @minPrice")

    if max_price is not None:
        parameters.append({"name": "@maxPrice", "value": float(max_price)})
        where_clauses.append("IS_DEFINED(c.price) AND c.price <= @maxPrice")

    query = "SELECT TOP @limit c.id, c.name, c.category, c.price, c.description FROM c"
    if where_clauses:
        query = f"{query} WHERE {' AND '.join(where_clauses)}"

    items = list(
        container.query_items(
            query=query,
            parameters=parameters,
            enable_cross_partition_query=True,
        )
    )

    return enforce_output_policy({"count": len(items), "items": items})


@mcp.tool()
def get_product_by_id(id: str) -> dict[str, Any] | None:
    """Get one product by id."""
    enforce_rate_limit("cosmos.get_product_by_id")
    enforce_text_policy(id, "id")

    container = get_container()
    item_id = (id or "").strip()
    if not item_id:
        raise ValueError("'id' is required.")

    items = list(
        container.query_items(
            query="SELECT TOP 1 * FROM c WHERE c.id = @id",
            parameters=[{"name": "@id", "value": item_id}],
            enable_cross_partition_query=True,
        )
    )

    return enforce_output_policy(items[0] if items else None)


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

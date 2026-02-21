using System.ComponentModel;
using Microsoft.Azure.Cosmos;
using ModelContextProtocol.Server;

internal class CosmosTools
{
    private static Microsoft.Azure.Cosmos.Container GetContainer()
    {
        var connectionString = Environment.GetEnvironmentVariable("COSMOS_CONNECTION_STRING");
        var databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") ?? "sales";
        var containerName = Environment.GetEnvironmentVariable("COSMOS_CONTAINER_NAME") ?? "products";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing COSMOS_CONNECTION_STRING environment variable.");
        }

        var client = new CosmosClient(connectionString);
        return client.GetContainer(databaseName, containerName);
    }

    [McpServerTool]
    [Description("Find products by optional text/category/price filters.")]
    public async Task<object> FindProducts(
        [Description("Search text for product name or description.")] string? searchText = null,
        [Description("Category filter.")] string? category = null,
        [Description("Minimum price.")] double? minPrice = null,
        [Description("Maximum price.")] double? maxPrice = null,
        [Description("Maximum number of items to return.")] int limit = 20)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        var baseSql = "SELECT TOP @limit c.id, c.name, c.category, c.price, c.description FROM c";

        var whereClauses = new List<string>();
        var queryParameters = new Dictionary<string, object?>
        {
            ["@limit"] = safeLimit
        };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            queryParameters["@search"] = searchText.Trim();
            whereClauses.Add("(CONTAINS(c.name, @search, true) OR CONTAINS(c.description, @search, true))");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            queryParameters["@category"] = category.Trim();
            whereClauses.Add("c.category = @category");
        }

        if (minPrice is not null)
        {
            queryParameters["@minPrice"] = minPrice.Value;
            whereClauses.Add("IS_DEFINED(c.price) AND c.price >= @minPrice");
        }

        if (maxPrice is not null)
        {
            queryParameters["@maxPrice"] = maxPrice.Value;
            whereClauses.Add("IS_DEFINED(c.price) AND c.price <= @maxPrice");
        }

        var queryText = whereClauses.Count > 0
            ? $"{baseSql} WHERE {string.Join(" AND ", whereClauses)}"
            : baseSql;

        var query = new QueryDefinition(queryText);
        foreach (var parameter in queryParameters)
        {
            query = query.WithParameter(parameter.Key, parameter.Value);
        }

        var container = GetContainer();
        using var iterator = container.GetItemQueryIterator<object>(query);

        var items = new List<object>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync().ConfigureAwait(false);
            items.AddRange(response);
            if (items.Count >= safeLimit)
            {
                break;
            }
        }

        return new
        {
            count = items.Count,
            items = items.Take(safeLimit).ToList()
        };
    }

    [McpServerTool]
    [Description("Get one product by id.")]
    public async Task<object?> GetProductById(
        [Description("Product id.")] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("'id' is required.", nameof(id));
        }

        var query = new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.id = @id")
            .WithParameter("@id", id.Trim());

        var container = GetContainer();
        using var iterator = container.GetItemQueryIterator<object>(query);

        if (!iterator.HasMoreResults)
        {
            return null;
        }

        var response = await iterator.ReadNextAsync().ConfigureAwait(false);
        return response.Resource.FirstOrDefault();
    }
}

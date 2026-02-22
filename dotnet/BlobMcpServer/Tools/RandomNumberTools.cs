using System.ComponentModel;
using System.Text;
using Azure.Storage.Blobs;
using ModelContextProtocol.Server;

internal class BlobTools
{
    private static BlobContainerClient GetContainerClient()
    {
        var connectionString = Environment.GetEnvironmentVariable("AZURE_BLOB_CONNECTION_STRING");
        var containerName = Environment.GetEnvironmentVariable("AZURE_BLOB_CONTAINER_NAME") ?? "semiconductor-product-documents";

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing AZURE_BLOB_CONNECTION_STRING environment variable.");
        }

        var service = new BlobServiceClient(connectionString);
        return service.GetBlobContainerClient(containerName);
    }

    [McpServerTool]
    [Description("List documents in the Azure Blob container.")]
    public object ListDocuments(
        [Description("Optional blob name prefix filter.")] string? prefix = null,
        [Description("Maximum number of items to return.")] int limit = 50)
    {
        var safeLimit = Math.Clamp(limit, 1, 200);
        var container = GetContainerClient();

        var items = new List<object>();
        foreach (var blob in container.GetBlobs(prefix: string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim()))
        {
            items.Add(new
            {
                name = blob.Name,
                size = blob.Properties.ContentLength,
                content_type = blob.Properties.ContentType,
                last_modified = blob.Properties.LastModified?.UtcDateTime.ToString("o")
            });

            if (items.Count >= safeLimit)
            {
                break;
            }
        }

        return new
        {
            count = items.Count,
            items
        };
    }

    [McpServerTool]
    [Description("Read one blob document; returns text if UTF-8 decodable, otherwise base64 content.")]
    public object ReadDocument(
        [Description("Blob name to read.")] string blobName,
        [Description("Maximum content characters to return.")] int maxChars = 20000)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("'blobName' is required.", nameof(blobName));
        }

        var safeMax = Math.Clamp(maxChars, 1, 200000);
        var container = GetContainerClient();
        var blob = container.GetBlobClient(blobName.Trim());

        using var stream = new MemoryStream();
        blob.DownloadTo(stream);
        var data = stream.ToArray();

        var properties = blob.GetProperties();
        var contentType = properties.Value.ContentType;

        try
        {
            var text = Encoding.UTF8.GetString(data);
            return new
            {
                blob_name = blobName.Trim(),
                content_type = contentType,
                encoding = "utf-8",
                truncated = text.Length > safeMax,
                content = text[..Math.Min(text.Length, safeMax)],
                size_bytes = data.Length
            };
        }
        catch
        {
            var encoded = Convert.ToBase64String(data);
            return new
            {
                blob_name = blobName.Trim(),
                content_type = contentType,
                encoding = "base64",
                truncated = encoded.Length > safeMax,
                content = encoded[..Math.Min(encoded.Length, safeMax)],
                size_bytes = data.Length
            };
        }
    }

    [McpServerTool]
    [Description("Download one blob document as a base64 payload with metadata.")]
    public object DownloadDocument(
        [Description("Blob name to download.")] string blobName,
        [Description("Maximum base64 characters to return.")] int maxChars = 5_000_000)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("'blobName' is required.", nameof(blobName));
        }

        var safeMax = Math.Clamp(maxChars, 10_000, 20_000_000);
        var trimmedName = blobName.Trim();

        var container = GetContainerClient();
        var blob = container.GetBlobClient(trimmedName);

        using var stream = new MemoryStream();
        blob.DownloadTo(stream);
        var data = stream.ToArray();

        var properties = blob.GetProperties();
        var contentType = properties.Value.ContentType;
        var base64 = Convert.ToBase64String(data);
        var fileName = Path.GetFileName(trimmedName);

        return new
        {
            blob_name = trimmedName,
            file_name = string.IsNullOrWhiteSpace(fileName) ? trimmedName : fileName,
            content_type = contentType,
            encoding = "base64",
            truncated = base64.Length > safeMax,
            content = base64[..Math.Min(base64.Length, safeMax)],
            size_bytes = data.Length
        };
    }
}

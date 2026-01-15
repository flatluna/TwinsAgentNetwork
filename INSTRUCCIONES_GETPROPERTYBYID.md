# ?? SOLUCIÓN SIMPLE - 2 Pasos para Completar GetPropertyById

## ? Resumen
- Archivo creado: `AzureFunctions/MiCasaPropertyFx.cs` ?
- Falta: Agregar método y clase al archivo `Services/AgentTwinMiCasaCosmosDB.cs`

---

## ?? PASO 1: Agregar el Método

Abre el archivo: `Services/AgentTwinMiCasaCosmosDB.cs`

Busca la línea **línea 867** que dice:
```csharp
public void Dispose()
{
    _cosmosClient?.Dispose();
}
```

**INMEDIATAMENTE DESPUÉS** del método `Dispose()`, pega este código:

```csharp
/// <summary>
/// Retrieves a specific property by its ID from a client document
/// Uses Cosmos DB JOIN to unnest the propiedad array and filter by property ID
/// </summary>
public async Task<GetPropertyByIdResult> GetPropertyByIdAsync(string clientId, string propiedadId)
{
    if (string.IsNullOrEmpty(clientId))
        return new GetPropertyByIdResult { Success = false, ErrorMessage = "Client ID cannot be null or empty", Propiedad = null };

    if (string.IsNullOrEmpty(propiedadId))
        return new GetPropertyByIdResult { Success = false, ErrorMessage = "Property ID cannot be null or empty", Propiedad = null };

    try
    {
        await InitializeCosmosClientAsync();
        var container = _cosmosClient.GetContainer(_databaseName, _containerName);

        string query = "SELECT p FROM c JOIN p IN c.propiedad WHERE c.id = @clientId AND p.id = @propiedadId";
        var queryDefinition = new QueryDefinition(query)
            .WithParameter("@clientId", clientId)
            .WithParameter("@propiedadId", propiedadId);

        using FeedIterator<dynamic> feed = container.GetItemQueryIterator<dynamic>(queryDefinition);

        LibModels.Propiedad propiedad = null;
        double totalRU = 0;

        while (feed.HasMoreResults)
        {
            FeedResponse<dynamic> response = await feed.ReadNextAsync();
            totalRU += response.RequestCharge;

            if (response.Count > 0)
            {
                var item = response.FirstOrDefault();
                if (item != null)
                {
                    string propiedadJson = JsonConvert.SerializeObject(item.p);
                    propiedad = JsonConvert.DeserializeObject<LibModels.Propiedad>(propiedadJson);
                    break;
                }
            }
        }

        if (propiedad == null)
        {
            Console.WriteLine($"?? Property not found: clientId={clientId}, propiedadId={propiedadId}");
            return new GetPropertyByIdResult { Success = false, ErrorMessage = $"Property with ID '{propiedadId}' not found in client '{clientId}'", Propiedad = null };
        }

        Console.WriteLine($"? Retrieved property: clientId={clientId}, propiedadId={propiedadId}. RU consumed: {totalRU:F2}");
        return new GetPropertyByIdResult { Success = true, ClientId = clientId, Propiedad = propiedad, RUConsumed = totalRU, Timestamp = DateTime.UtcNow };
    }
    catch (CosmosException cosmosEx)
    {
        Console.WriteLine($"? Cosmos DB error: {cosmosEx.Message} (Status: {cosmosEx.StatusCode})");
        return new GetPropertyByIdResult { Success = false, ErrorMessage = $"Cosmos DB error: {cosmosEx.Message}", Propiedad = null };
    }
    catch (Exception ex)
    {
        Console.WriteLine($"? Error retrieving property: {ex.Message}");
        return new GetPropertyByIdResult { Success = false, ErrorMessage = ex.Message, Propiedad = null };
    }
}
```

---

## ?? PASO 2: Agregar la Clase Result

En el **MISMO ARCHIVO**, busca la línea que dice `#region Data Models`  

Ve hasta el **FINAL** de esa región, justo **ANTES** de `#endregion` (alrededor de la línea 1152)

**DESPUÉS** de la clase `UpdatePropiedadResult` y **ANTES** de `#endregion`, pega:

```csharp
/// <summary>
/// Result of retrieving a specific property by ID from a client document
/// </summary>
public class GetPropertyByIdResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string ClientId { get; set; } = "";
    public LibModels.Propiedad? Propiedad { get; set; }
    public double RUConsumed { get; set; }
    public DateTime Timestamp { get; set; }
}
```

---

## ? Verificar

Ejecuta:
```bash
dotnet build
```

Deberías ver: **Build succeeded. 0 Error(s)**

---

## ?? Endpoint Disponible

```
GET /api/micasa/clients/{clientId}/properties/{propertyId}
```

### Ejemplo:
```bash
curl http://localhost:7071/api/micasa/clients/abc123/properties/prop456
```

### Respuesta:
```json
{
  "success": true,
  "clientId": "abc123",
  "propertyId": "prop456",
  "propiedad": {
    "id": "prop456",
    "tipoPropiedad": "Casa",
    "precio": 3500000
  },
  "ruConsumed": 2.85,
  "timestamp": "2025-01-16T12:00:00Z"
}
```

---

## ?? Notas Finales

- ? CORS configurado
- ? Validación de parámetros
- ? Manejo de errores (400, 404, 500)
- ? Logging con emojis
- ? RU tracking
- ? Usa `LibModels.Propiedad` de TwinAgentsLibrary

¡Listo en 2 minutos! ??

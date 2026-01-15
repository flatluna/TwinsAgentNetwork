using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Services;
using TwinAgentsNetwork.Services;
using OpenAI.Chat;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente especializado en procesar facturas de clientes de agentes inmobiliarios
    /// Extrae información completa de facturas usando Azure AI Document Intelligence y OpenAI
    /// </summary>
    public class AgentTwinFacturasClientes
    {
        private readonly ILogger<AgentTwinFacturasClientes> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ChatClient _chatClient;

        public AgentTwinFacturasClientes(ILogger<AgentTwinFacturasClientes> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("✅ DocumentIntelligenceService initialized successfully");

                // Get Azure OpenAI configuration
                var endpoint = configuration["Values:AzureOpenAI:Endpoint"] ??
                              configuration["AzureOpenAI:Endpoint"] ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");

                var apiKey = configuration["Values:AzureOpenAI:ApiKey"] ??
                            configuration["AzureOpenAI:ApiKey"] ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");

                var deploymentName = "gpt-5-mini";

                _logger.LogInformation("🔧 Using Azure OpenAI configuration:");
                _logger.LogInformation("   • Endpoint: {Endpoint}", endpoint);
                _logger.LogInformation("   • Deployment: {DeploymentName}", deploymentName);
                
                // Initialize Azure OpenAI client with extended timeout
                var clientOptions = new Azure.AI.OpenAI.AzureOpenAIClientOptions
                {
                    NetworkTimeout = TimeSpan.FromSeconds(600)
                };
                
                _azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey), clientOptions);
                _chatClient = _azureClient.GetChatClient(deploymentName);

                _logger.LogInformation("✅ Azure OpenAI clients initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize AgentTwinFacturasClientes");
                throw;
            }
        }

        /// <summary>
        /// Procesa una factura de cliente inmobiliario y extrae toda la información
        /// </summary>
        /// <param name="Language">Idioma para la respuesta (ej: "es", "en")</param>
        /// <param name="twinID">ID del twin/agente inmobiliario</param>
        /// <param name="filePath">Ruta del archivo en DataLake</param>
        /// <param name="fileName">Nombre del archivo de la factura</param>
        /// <param name="customerID">ID del cliente</param>
        /// <returns>Resultado con los datos extraídos de la factura</returns>
        public async Task<FacturaClienteResult> ProcesaFacturaClientesAsync(
            string Language,
            string twinID,
            string filePath,
            string fileName,
            string customerID)
        {
            _logger.LogInformation("🧾 Iniciando procesamiento de factura de cliente");
            _logger.LogInformation("📂 TwinID: {TwinID}, File: {FileName}, Customer: {CustomerID}", 
                twinID, fileName, customerID);

            var result = new FacturaClienteResult
            {
                Success = false,
                TwinID = twinID,
                FileName = fileName,
                CustomerID = customerID,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                // PASO 1: Generar SAS URL para acceso al documento
                _logger.LogInformation("🔗 PASO 1: Generando SAS URL para acceso al documento...");

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinID);
                var fullFilePath = $"{filePath}/{fileName}";
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(2));

                if (string.IsNullOrEmpty(sasUrl))
                {
                    result.ErrorMessage = "No se pudo generar SAS URL para acceso al documento";
                    _logger.LogError("❌ Failed to generate SAS URL for: {FullFilePath}", fullFilePath);
                    return result;
                }

                result.DocumentUrl = sasUrl;
                _logger.LogInformation("✅ SAS URL generada exitosamente");

                // PASO 2: Extraer datos usando Document Intelligence
                _logger.LogInformation("🤖 PASO 2: Extrayendo datos con Document Intelligence...");

                var documentAnalysis = await _documentIntelligenceService.AnalyzeDocumentWithPagesAsync(sasUrl);

                if (!documentAnalysis.Success)
                {
                    result.ErrorMessage = $"Document Intelligence falló: {documentAnalysis.ErrorMessage}";
                    _logger.LogError("❌ Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                    return result;
                }

                result.TotalPages = documentAnalysis.TotalPages;
                _logger.LogInformation("✅ Document Intelligence completado - {Pages} páginas extraídas", 
                    documentAnalysis.TotalPages);

                // PASO 3: Procesar cada página de la factura con IA
                _logger.LogInformation("🧠 PASO 3: Procesando contenido de la factura con OpenAI...");

                var facturaData = await ExtraerDatosFacturaConIA(documentAnalysis, Language);

                facturaData.NombreArchivo = fileName;
                facturaData.Path = filePath;
                facturaData.SASURL = sasUrl;

                if (facturaData == null)
                {
                    result.ErrorMessage = "No se pudo extraer datos de la factura con IA";
                    _logger.LogError("❌ Failed to extract invoice data with AI");
                    return result;
                }

                result.FacturaData = facturaData;
                result.Success = true;
                
                _logger.LogInformation("✅ Factura procesada exitosamente: {NumeroFactura}", facturaData.NumeroFactura);
                _logger.LogInformation("💰 Total: {Total} {Moneda}", facturaData.MontoTotal, facturaData.Moneda);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error procesando factura {FileName}", fileName);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Extrae datos de la factura usando OpenAI con prompt en español
        /// </summary>
        private async Task<FacturaClienteData> ExtraerDatosFacturaConIA(
            DocumentAnalysisResult documentAnalysis, 
            string idioma)
        {
            try
            {
                var documentContent = new StringBuilder();
                
                if (documentAnalysis.DocumentPages != null && documentAnalysis.DocumentPages.Count > 0)
                {
                    foreach (var page in documentAnalysis.DocumentPages)
                    {
                        documentContent.AppendLine($"\n--- PÁGINA {page.PageNumber} ---");
                        
                        if (page.LinesText != null && page.LinesText.Count > 0)
                        {
                            documentContent.AppendLine(string.Join("\n", page.LinesText));
                        }
                    }
                }

                if (documentAnalysis.Tables != null && documentAnalysis.Tables.Count > 0)
                {
                    documentContent.AppendLine("\n=== TABLAS EXTRAÍDAS ===");
                    
                    for (int i = 0; i < documentAnalysis.Tables.Count; i++)
                    {
                        var table = documentAnalysis.Tables[i];
                        documentContent.AppendLine($"\n--- TABLA {i + 1} ---");
                        
                        if (table.AsSimpleTable != null && table.AsSimpleTable.Rows != null && table.AsSimpleTable.Rows.Count > 0)
                        {
                            if (table.AsSimpleTable.Headers != null && table.AsSimpleTable.Headers.Count > 0)
                            {
                                documentContent.AppendLine(string.Join(" | ", table.AsSimpleTable.Headers));
                                documentContent.AppendLine(new string('-', 50));
                            }

                            foreach (var row in table.AsSimpleTable.Rows)
                            {
                                if (row != null && row.Count > 0)
                                {
                                    documentContent.AppendLine(string.Join(" | ", row));
                                }
                            }
                        }
                    }
                    
                    _logger.LogInformation("📊 Tablas incluidas en el prompt: {TableCount}", documentAnalysis.Tables.Count);
                }

                string contentText = documentContent.ToString();
                _logger.LogInformation("📝 Contenido preparado: {ContentLength} caracteres", contentText.Length);

                // Crear el prompt en español
                string prompt = CrearPromptExtraccionFactura(contentText, idioma);

                // Llamar a OpenAI usando el patrón correcto
                var chatMessages = new List<ChatMessage>
                {
                    new SystemChatMessage(CrearInstruccionesFactura(idioma)),
                    new UserChatMessage(prompt)
                };

                var chatOptions = new ChatCompletionOptions
                {
                    
                };

                var response = await _chatClient.CompleteChatAsync(chatMessages, chatOptions);
                
                if (response?.Value?.Content == null || response.Value.Content.Count == 0)
                {
                    _logger.LogError("❌ OpenAI returned empty response");
                    return null;
                }

                string aiResponse = response.Value.Content[0].Text;
                _logger.LogInformation("✅ OpenAI response received: {Length} characters", aiResponse.Length);

                // Parsear JSON de respuesta
                var facturaData = ParsearRespuestaFactura(aiResponse);
                
                return facturaData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extrayendo datos con IA");
                return null;
            }
        }

        /// <summary>
        /// Crea las instrucciones del sistema para OpenAI
        /// </summary>
        private string CrearInstruccionesFactura(string idioma)
        {
            return $@"
🎯 IDENTIDAD: Eres un EXPERTO EN EXTRACCIÓN DE DATOS DE FACTURAS INMOBILIARIAS.

🧠 TU ESPECIALIZACIÓN:
- Expertos en identificar datos de facturas de servicios inmobiliarios
- Especialistas en extraer información de comisiones y servicios
- Autoridades en análisis de documentos financieros del sector inmobiliario
- crea un resumen ejecutivo de la factura usando HTML que se ve colorido y con tablas bonitas, grids, titulos etc.
- Expertos en extraer datos estructurados de facturas en formato JSON

📋 REQUISITOS DE RESPUESTA:
1. IDIOMA: Responder en {idioma}
2. FORMATO: JSON válido únicamente, sin markdown
3. PRECISIÓN: Extraer todos los datos con exactitud
4. ESTRUCTURA: Seguir el formato JSON especificado
5. COMPLETITUD: No omitir ningún campo del formato

🔍 DATOS A EXTRAER:
- Número de factura (numero_factura)
- Fecha de emisión (fecha_emision)
- Nombre del cliente (nombre_cliente)
- ID/RFC del cliente (id_cliente)
- Email del cliente (email_cliente)
- Teléfono del cliente (telefono_cliente)
- Dirección del cliente (direccion_cliente)
- Nombre del agente inmobiliario (nombre_agente)
- Empresa/Agencia (nombre_agencia)
- Descripción de servicios (descripcion_servicios)
- Lista de conceptos con cantidad, descripción, precio unitario, total (conceptos)
- Subtotal (subtotal)
- IVA/Impuestos (monto_impuesto)
- Total (monto_total)
- Moneda (moneda)
- Condiciones de pago (condiciones_pago)
- Método de pago (metodo_pago)
- Notas adicionales (notas)

🎯 IMPORTANTE: Responde SOLO con JSON válido, sin bloques de código markdown.";
        }

        /// <summary>
        /// Crea el prompt para extraer datos de la factura
        /// </summary>
        private string CrearPromptExtraccionFactura(string contenidoDocumento, string idioma)
        {
            return $@"
Analiza la siguiente factura de servicios inmobiliarios y extrae TODA la información en formato JSON.

FORMATO JSON REQUERIDO:
{{
  ""numero_factura"": ""Número de factura"",
  ""fecha_emision"": ""Fecha de emisión (YYYY-MM-DD)"",
  ""nombre_cliente"": ""Nombre completo del cliente"",
  ""id_cliente"": ""RFC/ID del cliente"",
  ""email_cliente"": ""Email del cliente"",
  ""telefono_cliente"": ""Teléfono del cliente"",
  ""direccion_cliente"": ""Dirección completa del cliente"",
  ""nombre_agente"": ""Nombre del agente inmobiliario"",
  ""nombre_agencia"": ""Nombre de la agencia/empresa"",
  ""descripcion_servicios"": ""Descripción general de los servicios"",
  ""conceptos"": [
    {{
      ""cantidad"": 1,
      ""descripcion"": ""Descripción del servicio"",
      ""precio_unitario"": 0.00,
      ""total"": 0.00
    }}
  ],
  ""subtotal"": 0.00,
  ""monto_impuesto"": 0.00,
  ""tasa_impuesto"": 16.0,
  ""monto_total"": 0.00,
  ""moneda"": ""MXN"",
  ""condiciones_pago"": ""Condiciones de pago"",
  ""metodo_pago"": ""Método de pago"",
  ""notas"": ""Notas o comentarios adicionales"",
  ""resumen_ejecutivo_html"": ""HTML colorido bonito grids, tablas, titulos de la factura etc.""
}}

CONTENIDO DE LA FACTURA:
{contenidoDocumento}

INSTRUCCIONES:
1. Extrae TODOS los datos que encuentres en la factura
2. Si un dato no está presente, usa un string vacío """" o 0 para números
3. Responde ÚNICAMENTE con JSON válido
4. NO uses bloques de código markdown (```json)
5. Asegúrate de que todos los números sean numéricos, no strings
6. Las fechas en formato YYYY-MM-DD
7. Extrae TODOS los conceptos/líneas de la factura en conceptos

Responde SOLO con el JSON:";
        }

        /// <summary>
        /// Parsea la respuesta JSON de OpenAI
        /// </summary>
        private FacturaClienteData ParsearRespuestaFactura(string aiResponse)
        {
            try
            {
                // Limpiar respuesta (remover markdown si existe)
                string jsonContent = aiResponse.Trim();
                
                if (jsonContent.StartsWith("```json"))
                {
                    jsonContent = jsonContent.Replace("```json", "").Replace("```", "").Trim();
                }
                else if (jsonContent.StartsWith("```"))
                {
                    jsonContent = jsonContent.Replace("```", "").Trim();
                }

                // Deserializar JSON
                var facturaData = System.Text.Json.JsonSerializer.Deserialize<FacturaClienteData>(
                    jsonContent, 
                    new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });

                _logger.LogInformation("✅ Factura parseada exitosamente: {NumeroFactura}", facturaData?.NumeroFactura);
                
                return facturaData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error parseando respuesta JSON de factura");
                _logger.LogError("Respuesta recibida: {Response}", aiResponse);
                return null;
            }
        }
    }

    #region Data Models

    /// <summary>
    /// Resultado del procesamiento de factura de cliente
    /// </summary>
    public class FacturaClienteResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string CustomerID { get; set; } = string.Empty;
        public string DocumentUrl { get; set; } = string.Empty;
        public int TotalPages { get; set; }
        public DateTime ProcessedAt { get; set; }
        public FacturaClienteData FacturaData { get; set; }
    }

    /// <summary>
    /// Datos completos de una factura de cliente inmobiliario
    /// </summary>
    public class FacturaClienteData
    {
        [System.Text.Json.Serialization.JsonPropertyName("numero_factura")]
        public string NumeroFactura { get; set; } = string.Empty;

        public string id { get; set; } = string.Empty;



        [System.Text.Json.Serialization.JsonPropertyName("resumen_ejecutivo_html")]
        public string ResumenEjecutuvoHTML { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("fecha_emision")]
        public string FechaEmision { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("nombre_cliente")]
        public string NombreCliente { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("id_cliente")]
        public string IdCliente { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("email_cliente")]
        public string EmailCliente { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("telefono_cliente")]
        public string TelefonoCliente { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("direccion_cliente")]
        public string DireccionCliente { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("nombre_agente")]
        public string NombreAgente { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("nombre_agencia")]
        public string NombreAgencia { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("descripcion_servicios")]
        public string DescripcionServicios { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("conceptos")]
        public List<FacturaLineItem> Conceptos { get; set; } = new();
        
        [System.Text.Json.Serialization.JsonPropertyName("subtotal")]
        public decimal Subtotal { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("monto_impuesto")]
        public decimal MontoImpuesto { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("tasa_impuesto")]
        public decimal TasaImpuesto { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("monto_total")]
        public decimal MontoTotal { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("moneda")]
        public string Moneda { get; set; } = "MXN";
        
        [System.Text.Json.Serialization.JsonPropertyName("condiciones_pago")]
        public string CondicionesPago { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("metodo_pago")]
        public string MetodoPago { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("notas")]
        public string Notas { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("nombreArchivo")]
        public string NombreArchivo { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("sasurl")]
        public string SASURL { get; set; } = string.Empty;
    }

    /// <summary>
    /// Línea individual de concepto en la factura
    /// </summary>
    public class FacturaLineItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("cantidad")]
        public int Cantidad { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("descripcion")]
        public string Descripcion { get; set; } = string.Empty;
        
        [System.Text.Json.Serialization.JsonPropertyName("precio_unitario")]
        public decimal PrecioUnitario { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("total")]
        public decimal Total { get; set; }
    }

    #endregion
}

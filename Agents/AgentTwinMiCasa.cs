using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinAgentsLibrary.Models;
using TwinAgentsNetwork.AzureFunctions;
using TwinAgentsNetwork.Models;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.Agents
{
    internal class AgentTwinMiCasa
    {

        private static readonly HttpClient client = new HttpClient();
        private readonly ILogger<AgentTwinMiCasa> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureOpenAIClient;
        private readonly ChatClient _chatClient;
        private readonly MiCasaFotosIndex _fotosIndex;

        public AgentTwinMiCasa(ILogger<AgentTwinMiCasa> logger, IConfiguration configuration, MiCasaFotosIndex fotosIndex)
        {
            _logger = logger;
            _configuration = configuration;
            _fotosIndex = fotosIndex ?? throw new ArgumentNullException(nameof(fotosIndex));

            try
            {
                // Configurar Azure OpenAI para análisis de imágenes
                var endpoint = _configuration["Values:AzureOpenAI:Endpoint"] ?? _configuration["AzureOpenAI:Endpoint"] ?? "";
                var apiKey = _configuration["Values:AzureOpenAI:ApiKey"] ?? _configuration["AzureOpenAI:ApiKey"] ?? "";
                var visionModelName = _configuration["Values:AZURE_OPENAI_VISION_MODEL"] ?? _configuration["AZURE_OPENAI_VISION_MODEL"] ?? "gpt-4o-mini";
                visionModelName = "gpt-5-mini"; // Temporalmente forzar uso de gpt5mini hasta que gpt-4o-mini soporte imágenes
                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException("Azure OpenAI endpoint and API key are required for photo analysis");
                }

                _azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                _chatClient = _azureOpenAIClient.GetChatClient(visionModelName);

                _logger.LogInformation("✅ MiMemoriaAgent initialized successfully with model: {ModelName}", visionModelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize MiMemoriaAgent");
                throw;
            }
        }

        /// <summary>
        /// Genera un análisis completo de una propiedad integrando datos de cliente, propiedad y análisis de fotos
        /// 1. Obtiene datos del cliente y propiedad desde Cosmos DB
        /// 2. Recupera todos los análisis de fotos de la propiedad desde el índice de búsqueda
        /// 3. Agrupa análisis por sección
        /// 4. Genera con OpenAI: sumario ejecutivo, descripción detallada y HTML
        /// </summary>
        /// <param name="twinId">ID del Twin propietario</param>
        /// <param name="propiedadId">ID de la propiedad a analizar</param>
        /// <param name="casaId">ID de la casa (documento del cliente)</param>
        /// <returns>Objeto con sumario ejecutivo, descripción detallada y HTML</returns>
        public async Task<PropertyComprehensiveAnalysisResult> GeneratePropertyComprehensiveAnalysisAsync(
            string twinId, 
            string propiedadId, 
            string casaId)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                // Validar parámetros de entrada
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(propiedadId) || string.IsNullOrEmpty(casaId))
                {
                    throw new ArgumentException("Twin ID, Property ID, and Casa ID are required");
                }

                _logger.LogInformation("🏠 Starting comprehensive property analysis for Twin: {TwinId}, Property: {PropiedadId}, Casa: {CasaId}",
                    twinId, propiedadId, casaId);

                // PASO 1: Obtener datos del cliente y propiedad desde Cosmos DB
                _logger.LogInformation("📊 Fetching client and property data from Cosmos DB...");
                var cosmosDb = new AgentTwinMiCasaCosmosDB();
                var clientResult = await cosmosDb.GetClientByIdAsync(casaId);

                if (!clientResult.Success || clientResult.Client == null)
                {
                    throw new Exception($"Failed to retrieve client data: {clientResult.ErrorMessage}");
                }

                var cliente = clientResult.Client;
                var propiedad = cliente.Propiedad?.FirstOrDefault(p => p.Id == propiedadId);

                if (propiedad == null)
                {
                    throw new Exception($"Property {propiedadId} not found for client {casaId}");
                }

                _logger.LogInformation("✅ Client and property data retrieved successfully");

                // PASO 2: Recuperar todos los análisis de fotos de la propiedad desde el índice de búsqueda
                _logger.LogInformation("📸 Fetching photo analysis from search index...");
                var photosResult = await _fotosIndex.GetPhotosByCasaIdPropiedadIdAsync(casaId, twinId,propiedadId);

                if (!photosResult.Success)
                {
                    throw new Exception($"Failed to retrieve photo analysis: {photosResult.Error}");
                }

                _logger.LogInformation("✅ Retrieved {PhotoCount} photo analyses", photosResult.TotalCount);

                // PASO 3: Agrupar análisis por sección/tipo
                var photosBySection = GroupPhotosBySection(photosResult.Documents);

                // PASO 4: Crear prompt con toda la información y llamar a OpenAI
                var analysisPrompt = CreatePropertyAnalysisPrompt(cliente, propiedad, photosBySection);

                
                _logger.LogInformation("🤖 Sending property analysis request to OpenAI...");

                // Llamar a OpenAI para generar el análisis completo
                var message = ChatMessage.CreateUserMessage(analysisPrompt);
                var chatOptions = new ChatCompletionOptions
                {
                   // Temperature = 0.7f
                };

                var response = await _chatClient.CompleteChatAsync(new[] { message }, chatOptions);

                if (response?.Value?.Content?.Count == 0)
                {
                    throw new Exception("Empty response from OpenAI");
                }

                var aiResponse = response.Value.Content[0].Text;
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("✅ Received comprehensive analysis from OpenAI in {ProcessingTime}ms", processingTime);

                // Parsear respuesta JSON de OpenAI
                var analysisResult = JsonConvert.DeserializeObject<PropertyComprehensiveAnalysisResult>(aiResponse);
                
                if (analysisResult == null)
                {
                    analysisResult = new PropertyComprehensiveAnalysisResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to parse OpenAI response"
                    };
                }
                else
                {
                    propiedad.AnalysisResult = analysisResult;
                    
                    // Update the property in the client's property list
                    var propIndex = clientResult.Client.Propiedad.FindIndex(p => p.Id == propiedad.Id);
                    if (propIndex >= 0)
                    {
                        clientResult.Client.Propiedad[propIndex] = propiedad;
                    }
                        
                    await cosmosDb.UpdateMiCasaSaveResultAsync(clientResult.Client.Id, clientResult.Client);
                    analysisResult.Success = true;
                    analysisResult.ProcessingTimeMs = processingTime;
                    analysisResult.TotalPhotosAnalyzed = (int)photosResult.TotalCount;
                    analysisResult.TwinId = twinId;
                    analysisResult.PropiedadId = propiedadId;
                }

                _logger.LogInformation("✅ Comprehensive property analysis completed successfully");
                return analysisResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error generating comprehensive property analysis");
                return new PropertyComprehensiveAnalysisResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    TwinId = twinId,
                    PropiedadId = propiedadId
                };
            }
        }

        /// <summary>
        /// Agrupa los análisis de fotos por tipo de sección
        /// </summary>
        private Dictionary<string, List<MiCasaPhotoDocument>> GroupPhotosBySection(List<MiCasaPhotoDocument> documents)
        {
            var grouped = new Dictionary<string, List<MiCasaPhotoDocument>>();

            foreach (var doc in documents)
            {
                string tipoSeccion = doc.TipoSeccion ?? "Sin especificar";
                
                if (!grouped.ContainsKey(tipoSeccion))
                {
                    grouped[tipoSeccion] = new List<MiCasaPhotoDocument>();
                }
                
                grouped[tipoSeccion].Add(doc);
            }

            return grouped;
        }

        /// <summary>
        /// Crea el prompt comprehensivo para OpenAI con datos del cliente, propiedad y análisis de fotos
        /// </summary>
        private string CreatePropertyAnalysisPrompt(
            MiCasaClientes cliente, 
            dynamic propiedad, 
            Dictionary<string, List<MiCasaPhotoDocument>> photosBySection)
        {
            var promptBuilder = new StringBuilder();

            // Información del cliente
            string nombreCliente = cliente.NombreCliente ?? "";
            string apellidoCliente = cliente.ApellidoCliente ?? "";
            string tipoCliente = cliente.TipoCliente ?? "";
            string presupuesto = cliente.Presupuesto.ToString() ?? "";

            // Información de la propiedad
            string direccion = propiedad.Direccion?.Completa ?? "Sin especificar";
            string tipoPropiedad = propiedad.TipoPropiedad ?? "casa";
            string tipoOperacion = propiedad.TipoOperacion ?? "";
            string precio = propiedad.Precio?.ToString() ?? "";
            string metrosConstruidos = propiedad.Caracteristicas?.MetrosConstruidos?.ToString() ?? "No especificado";
            string metrosTerreno = propiedad.Caracteristicas?.MetrosTerreno?.ToString() ?? "No especificado";
            string numRecamaras = propiedad.Caracteristicas?.NumRecamaras?.ToString() ?? "No especificado";
            string numBanos = propiedad.Caracteristicas?.NumBanos?.ToString() ?? "No especificado";
            string numEstacionamientos = propiedad.Caracteristicas?.NumEstacionamientos?.ToString() ?? "No especificado";
            string descripcionPropiedad = propiedad.Descripcion ?? "";
            string motivoVenta = propiedad.MotivoVenta ?? "";

            // Amenidades
            var amenidades = (propiedad.Amenidades as System.Collections.IEnumerable)?.Cast<string>().ToList() ?? new List<string>();

            // Agrupar análisis de fotos por sección
            var seccionesInfo = new StringBuilder();
            int totalFotos = 0;
            double metrosCuadradosTotales = 0;
            var detallesSeccion = new StringBuilder();

            foreach (var seccion in photosBySection)
            {
                string tipoSeccion = seccion.Key;
                var fotos = seccion.Value;
                totalFotos += fotos.Count;

                seccionesInfo.AppendLine($"\n📸 {tipoSeccion} ({fotos.Count} foto(s)):");

                foreach (var foto in fotos)
                {
                    string nombreSeccion = foto.NombreSeccion ?? tipoSeccion;
                    string analisisDetallado = foto.AnalisisDetallado ?? "";
                    double? metrosCuadrados = foto.DimensionesAncho.HasValue && foto.DimensionesLargo.HasValue
                        ? foto.DimensionesAncho.Value * foto.DimensionesLargo.Value
                        : null;

                    if (metrosCuadrados.HasValue)
                    {
                        metrosCuadradosTotales += metrosCuadrados.Value;
                    }

                    seccionesInfo.AppendLine($"  - {nombreSeccion}");
                    if (metrosCuadrados.HasValue)
                    {
                        seccionesInfo.AppendLine($"    Superficie: {metrosCuadrados:F2} m²");
                    }
                    detallesSeccion.AppendLine($"\n aseguate de espicificar en que piso esta esta seccion " +
                        $" este es el  Piso : {foto.Piso} - {nombreSeccion}");
                    detallesSeccion.AppendLine($"\n### {tipoSeccion} - {nombreSeccion}");
                    detallesSeccion.AppendLine(analisisDetallado);
                }
            }

            // Construir el prompt
            promptBuilder.AppendLine("ERES UN EXPERTO INMOBILIARIO PROFESIONAL ESPECIALIZADO CREAR MERCADOTECNIA PARA " +
                "AGENTES DE VENTAS DE CASAS. Esta es una de esas casas");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("📋 INFORMACIÓN DEL CLIENTE:");
            promptBuilder.AppendLine($"Nombre: {nombreCliente} {apellidoCliente}");
            promptBuilder.AppendLine($"Tipo de Cliente: {tipoCliente}");
            promptBuilder.AppendLine($"Presupuesto: ${presupuesto}");
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("🏠 INFORMACIÓN DE LA PROPIEDAD:");
            promptBuilder.AppendLine($"Dirección: {direccion}");
            promptBuilder.AppendLine($"Tipo: {tipoPropiedad.ToUpper()}");
            promptBuilder.AppendLine($"Operación: {tipoOperacion}");
            promptBuilder.AppendLine($"Precio: ${precio}");
            promptBuilder.AppendLine($"Metros Construidos: {metrosConstruidos}");
            promptBuilder.AppendLine($"Metros de Terreno: {metrosTerreno}");
            promptBuilder.AppendLine($"Recámaras: {numRecamaras}");
            promptBuilder.AppendLine($"Baños: {numBanos}");
            promptBuilder.AppendLine($"Estacionamientos: {numEstacionamientos}");
            promptBuilder.AppendLine($"Descripción: {descripcionPropiedad}");
            promptBuilder.AppendLine($"Motivo de Venta: {motivoVenta}");
            promptBuilder.AppendLine($"Motivo de Venta: {propiedad}");

            if (amenidades.Count > 0)
            {
                promptBuilder.AppendLine($"Amenidades: {string.Join(", ", amenidades)}");
            }
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("📸 ANÁLISIS DE FOTOS POR SECCIÓN:");
            promptBuilder.Append(seccionesInfo);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("📊 DETALLES DE ANÁLISIS POR SECCIÓN:");
            promptBuilder.Append(detallesSeccion);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("📋 INSTRUCCIONES PARA EL ANÁLISIS:");
            promptBuilder.AppendLine("Basándote en toda la información proporcionada (datos del cliente, propiedad y análisis de fotos), genera un análisis comprehensivo con:");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("1. SUMARIO EJECUTIVO (máx 300 palabras):");
            promptBuilder.AppendLine("   - Descripción breve de la propiedad");
            promptBuilder.AppendLine("   - Punto de vista general sobre su estado y calidad");
            promptBuilder.AppendLine("   - Fortalezas y debilidades principales");
            promptBuilder.AppendLine("   - Recomendación general para potenciales compradores");
            promptBuilder.AppendLine();
             

            promptBuilder.AppendLine("3. DESCRIPCIÓN EN HTML (completa con estilos CSS inline):");
            promptBuilder.AppendLine("   - Análisis completo de cada sección/cuarto");
            promptBuilder.AppendLine("   - Estado general de pisos, paredes, techo");
            promptBuilder.AppendLine("   - Mobiliario y decoración observada");
            promptBuilder.AppendLine("   - Iluminación y ventilación");
            promptBuilder.AppendLine("   - Problemas identificados y potencial de mejora");
            promptBuilder.AppendLine("   - Total de metros cuadrados: Suma todos los m² de cada sección");
            promptBuilder.AppendLine("   - Total de cuartos identificados en las fotos");
            promptBuilder.AppendLine("   - En cada seccion como sala , comedor ordenalo por piso ejemplo " +
                "Titulo: Planta baja -- contiene : una sala hermosa , comedor para x personas etc. ");
            promptBuilder.AppendLine($"   - Metros calculados de análisis de fotos: {metrosCuadradosTotales:F2} m²");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("   - Usa HTML5 válido");
            promptBuilder.AppendLine("   - Incluye estilos CSS inline profesionales");
            promptBuilder.AppendLine("   - Estructura con headers, secciones claramente definidas");
            promptBuilder.AppendLine("   - Tabla resumen con características principales");
            promptBuilder.AppendLine("   - Listados de amenidades y características");
            promptBuilder.AppendLine("   - Colores profesionales: #2c3e50 (azul), #27ae60 (verde), #e74c3c (rojo)");
            promptBuilder.AppendLine("   - Que sea visualmente atractivo y fácil de leer");
            promptBuilder.AppendLine("   - La desripcion detallada y completa hazlo solo en html. Crea una descripcion que va a leer una persoan que va a comprar la casa asu" +
                "que tiene que ser una descripcion para mercadotecnia que ayude a vender la casa. No pongas cosas negativas o defectos de la casa" +
                "al contrario crea una descripcion super detallada con titulos, colores, etc. ");
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("RESPONDE EN FORMATO JSON VÁLIDO (sin ```json ni ```):");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"sumarioEjecutivo\": \"Texto del sumario ejecutivo...\","); 
            promptBuilder.AppendLine("  \"htmlCompleto\": \"<html>...</html>\",");
            promptBuilder.AppendLine("  \"totalMetrosCuadrados\": 250,");
            promptBuilder.AppendLine("  \"totalCuartos\": 5,");
            promptBuilder.AppendLine("  \"observacionesAdicionales\": \"Comentarios finales no inventes nada solo basate en la informaicon que se te da \",");
            promptBuilder.AppendLine("  \"recomendaciones\": [\"recomendación 1\", \"recomendación 2\", ...]");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("⚠️ INSTRUCCIONES CRÍTICAS:");
            promptBuilder.AppendLine("✓ Basa tu análisis ÚNICAMENTE en los datos proporcionados");
            promptBuilder.AppendLine("✓ Sé profesional y objetivo");
            promptBuilder.AppendLine("✓ Incluye detalles específicos de las fotos analizadas");
            promptBuilder.AppendLine("✓ Calcula correctamente metros cuadrados totales");
            promptBuilder.AppendLine("✓ El HTML debe ser completo y presentable");
            promptBuilder.AppendLine("✓ Escribe en español");
            promptBuilder.AppendLine("✓ JSON válido sin marcas de código");

            return promptBuilder.ToString();
        }

        /// <summary>
        /// Analiza una foto usando la URL SAS y genera una descripción detallada
        /// Retorna la información estructurada en el formato MiCasaPhotoAnalysisResult esperado
        /// </summary>
        /// <param name="sasUrl">URL SAS de la foto en Data Lake</param>
        /// <param name="analysisRequest">Solicitud de análisis con propiedades y observaciones de la sección</param>
        /// <returns>Objeto MiCasaPhotoAnalysisResult con análisis completo de la imagen</returns>
        public async Task<MiCasaPhotoAnalysisResult> AnalyzePhotoSimpleFormatAsync(string sasUrl,
            MiCasaFotoAnalysisRequest analysisRequest)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                // Validar entrada
                if (string.IsNullOrEmpty(sasUrl))
                {
                    throw new ArgumentException("SAS URL is required for photo analysis");
                }

                if (analysisRequest == null)
                {
                    throw new ArgumentException("Analysis request is required for photo analysis");
                }

                // Descargar la imagen usando la URL SAS
                byte[] imageBytes;
                string mediaType = "image/jpeg";

                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    var httpResponse = await httpClient.GetAsync(sasUrl);

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        throw new Exception($"Failed to download image from SAS URL: HTTP {httpResponse.StatusCode}");
                    }

                    imageBytes = await httpResponse.Content.ReadAsByteArrayAsync();
                    
                    // Detectar tipo de media correctamente
                    var contentType = httpResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                    mediaType = DetectImageMediaType(imageBytes, contentType);
                    
                    _logger.LogInformation("📸 Image ContentType header: {ContentType}, Detected MediaType: {MediaType}, Size: {Size} bytes", 
                        contentType, mediaType, imageBytes.Length);
                    
                    // Log first 32 bytes para debugging
                    var hexPreview = string.Join(" ", imageBytes.Take(32).Select(b => b.ToString("X2")));
                    _logger.LogInformation("📸 Image hex preview (first 32 bytes): {HexPreview}", hexPreview);
                }

                if (imageBytes.Length == 0)
                {
                    throw new Exception("Downloaded image is empty");
                }

                _logger.LogInformation("📸 Image downloaded successfully: {Size} bytes, Type: {MediaType}", 
                    imageBytes.Length, mediaType);

                // Detectar si es HTML de error (página de error de autenticación, etc.)
                if (IsHtmlContent(imageBytes))
                {
                    var htmlPreview = Encoding.UTF8.GetString(imageBytes.Take(500).ToArray());
                    _logger.LogError("❌ Downloaded content appears to be HTML, not an image. SAS URL may be invalid or expired.\nPreview: {HtmlPreview}", 
                        htmlPreview);
                    throw new Exception("Downloaded content is HTML (likely authentication error or invalid SAS URL), not a valid image file");
                }

                // Validar que la imagen sea válida
                if (!IsValidImageData(imageBytes, mediaType))
                {
                    throw new Exception($"Invalid or corrupted image data. Size: {imageBytes.Length} bytes, Type: {mediaType}, First bytes: {string.Join(" ", imageBytes.Take(8).Select(b => b.ToString("X2")))}");
                }

                // Crear el prompt personalizado para el análisis extrayendo todos los datos
                var analysisPrompt = CreateSimplePhotoAnalysisPrompt(analysisRequest);

                _logger.LogInformation("🤖 Creating image message with {Size} bytes image...", imageBytes.Length);

                // Crear mensaje con imagen para Azure OpenAI Vision
                var imageMessage = ChatMessage.CreateUserMessage(
                    ChatMessageContentPart.CreateTextPart(analysisPrompt),
                    ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), mediaType)
                );

                _logger.LogInformation("🤖 Sending image to Azure OpenAI Vision for analysis...");

                // Configurar opciones del chat
                var chatOptions = new ChatCompletionOptions
                {
                  //  Temperature = 0.7f
                };

                // Llamar a Azure OpenAI Vision API
                var visionResponse = await _chatClient.CompleteChatAsync(new[] { imageMessage }, chatOptions);

                if (visionResponse?.Value?.Content?.Count > 0)
                {
                    var aiResponse = visionResponse.Value.Content[0].Text;
                    var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                    _logger.LogInformation("✅ Received response from Azure OpenAI Vision, length: {Length} chars in {ProcessingTime}ms",
                        aiResponse.Length, processingTime);
                    
                    var analysisResult = JsonConvert.DeserializeObject<MiCasaPhotoAnalysisResult>(aiResponse);
                    analysisResult.Piso = analysisRequest.Piso;
                    // Upload analysis result to search index
                    if (analysisResult != null)
                    {
                        try
                        {
                            _logger.LogInformation("📷 Uploading photo analysis to search index...");
                            var uploadResult = await _fotosIndex.UploadPhotoAnalysisToIndexAsync(
                                twinId: analysisRequest.TwinId,
                                tipoSeccion: analysisRequest.TipoSeccion,
                                nombreSeccion: analysisRequest.NombreSeccion,
                                analysis: analysisResult,
                                propiedadId: analysisRequest.PropiedadId,
                                casaId: analysisRequest.CasaId,
                                fileName: analysisRequest.UploadedFile?.FileName,
                                containerName: analysisRequest.ContainerName,
                                filePath: analysisRequest.FilePath,
                                fileSize: analysisRequest.UploadedFile?.Length,
                                fileUploadedAt: analysisRequest.FileUploadedAt);

                            if (uploadResult.Success)
                            {
                                _logger.LogInformation("✅ Photo analysis uploaded to index successfully: DocumentId={DocumentId}", 
                                    uploadResult.DocumentId);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Failed to upload photo analysis to index: {Error}", 
                                    uploadResult.Error);
                            }
                        }
                        catch (Exception indexEx)
                        {
                            _logger.LogWarning(indexEx, "⚠️ Error uploading to search index, continuing with analysis result");
                        }
                    }

                    _logger.LogInformation("✅ Photo analysis completed successfully");
                    return analysisResult;
                }
                else
                {
                    throw new Exception("Empty response received from Azure OpenAI Vision");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error analyzing photo");

                // Retornar estructura básica en caso de error
                return new MiCasaPhotoAnalysisResult();
            }
        }

        /// <summary>
        /// Detecta el tipo de media correcto basándose en los bytes de la imagen
        /// </summary>
        private string DetectImageMediaType(byte[] imageBytes, string headerContentType)
        {
            if (imageBytes == null || imageBytes.Length < 2)
                return "image/jpeg";

            // Verificar firmas de archivo (magic numbers)
            
            // JPEG: FF D8 FF
            if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes.Length > 2 && imageBytes[2] == 0xFF)
                return "image/jpeg";

            // PNG: 89 50 4E 47
            if (imageBytes.Length >= 4 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
                return "image/png";

            // GIF: 47 49 46 (GIF87a or GIF89a)
            if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46)
                return "image/gif";

            // WebP: RIFF ... WEBP (52 49 46 46 ... 57 45 42 50)
            if (imageBytes.Length >= 12 && imageBytes[0] == 0x52 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46 && imageBytes[3] == 0x46)
            {
                if (imageBytes[8] == 0x57 && imageBytes[9] == 0x45 && imageBytes[10] == 0x42 && imageBytes[11] == 0x50)
                    return "image/webp";
            }

            // BMP: 42 4D (BM)
            if (imageBytes[0] == 0x42 && imageBytes[1] == 0x4D)
                return "image/bmp";

            // TIFF (Little Endian): 49 49 2A 00
            if (imageBytes.Length >= 4 && imageBytes[0] == 0x49 && imageBytes[1] == 0x49 && imageBytes[2] == 0x2A && imageBytes[3] == 0x00)
                return "image/tiff";

            // TIFF (Big Endian): 4D 4D 00 2A
            if (imageBytes.Length >= 4 && imageBytes[0] == 0x4D && imageBytes[1] == 0x4D && imageBytes[2] == 0x00 && imageBytes[3] == 0x2A)
                return "image/tiff";

            // ICO: 00 00 01 00
            if (imageBytes.Length >= 4 && imageBytes[0] == 0x00 && imageBytes[1] == 0x00 && imageBytes[2] == 0x01 && imageBytes[3] == 0x00)
                return "image/x-icon";

            // SVG: 3C 3F 78 6D 6C or 3C 73 76 67 (<?xml or <svg)
            if (imageBytes.Length >= 4)
            {
                if ((imageBytes[0] == 0x3C && imageBytes[1] == 0x3F && imageBytes[2] == 0x78 && imageBytes[3] == 0x6D) ||
                    (imageBytes[0] == 0x3C && imageBytes[1] == 0x73 && imageBytes[2] == 0x76 && imageBytes[3] == 0x67))
                    return "image/svg+xml";
            }

            // HEIF/HEIC: ftyp in header (usually at position 4-8)
            if (imageBytes.Length >= 12 && imageBytes[4] == 0x66 && imageBytes[5] == 0x74 && imageBytes[6] == 0x79 && imageBytes[7] == 0x70)
            {
                if (imageBytes[8] == 0x68 && imageBytes[9] == 0x65 && imageBytes[10] == 0x69 && imageBytes[11] == 0x63)
                    return "image/heic";
                if (imageBytes[8] == 0x68 && imageBytes[9] == 0x65 && imageBytes[10] == 0x69 && imageBytes[11] == 0x66)
                    return "image/heif";
            }

            // AVIF: ftyp ... av01
            if (imageBytes.Length >= 12 && imageBytes[4] == 0x66 && imageBytes[5] == 0x74 && imageBytes[6] == 0x79 && imageBytes[7] == 0x70)
            {
                if (imageBytes.Length >= 12 && imageBytes[8] == 0x61 && imageBytes[9] == 0x76 && imageBytes[10] == 0x30 && imageBytes[11] == 0x31)
                    return "image/avif";
            }

            // Si el header tiene un tipo válido, usarlo
            if (!string.IsNullOrEmpty(headerContentType) && 
                (headerContentType.StartsWith("image/") || headerContentType.Contains("image")))
                return headerContentType;

            // Default a JPEG si no se puede detectar
            return "image/jpeg";
        }

        /// <summary>
        /// Valida que los datos de la imagen sean válidos
        /// </summary>
        private bool IsValidImageData(byte[] imageBytes, string mediaType)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return false;

            // Tamaño mínimo para una imagen (al menos 100 bytes)
            if (imageBytes.Length < 100)
            {
                _logger.LogWarning("⚠️ Image size too small: {Size} bytes", imageBytes.Length);
                return false;
            }

            // Tamaño máximo (20 MB)
            if (imageBytes.Length > 20 * 1024 * 1024)
            {
                _logger.LogWarning("⚠️ Image size too large: {Size} bytes", imageBytes.Length);
                return false;
            }

            // Verificar que sea una imagen válida según su tipo
            if (mediaType.Contains("jpeg") || mediaType.Contains("jpg"))
                return imageBytes.Length >= 2 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8;

            if (mediaType.Contains("png"))
                return imageBytes.Length >= 4 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47;

            if (mediaType.Contains("gif"))
                return imageBytes.Length >= 3 && imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46;

            if (mediaType.Contains("webp"))
                return imageBytes.Length >= 12 && imageBytes[0] == 0x52 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46 && imageBytes[3] == 0x46;

            if (mediaType.Contains("bmp"))
                return imageBytes.Length >= 2 && imageBytes[0] == 0x42 && imageBytes[1] == 0x4D;

            if (mediaType.Contains("tiff"))
                return imageBytes.Length >= 4 && 
                    ((imageBytes[0] == 0x49 && imageBytes[1] == 0x49 && imageBytes[2] == 0x2A && imageBytes[3] == 0x00) ||
                     (imageBytes[0] == 0x4D && imageBytes[1] == 0x4D && imageBytes[2] == 0x00 && imageBytes[3] == 0x2A));

            if (mediaType.Contains("x-icon") || mediaType.Contains("ico"))
                return imageBytes.Length >= 4 && imageBytes[0] == 0x00 && imageBytes[1] == 0x00 && imageBytes[2] == 0x01 && imageBytes[3] == 0x00;

            if (mediaType.Contains("svg"))
                return imageBytes.Length >= 4 && 
                    ((imageBytes[0] == 0x3C && imageBytes[1] == 0x3F && imageBytes[2] == 0x78 && imageBytes[3] == 0x6D) ||
                     (imageBytes[0] == 0x3C && imageBytes[1] == 0x73 && imageBytes[2] == 0x76 && imageBytes[3] == 0x67));

            if (mediaType.Contains("heic"))
                return imageBytes.Length >= 12 && imageBytes[4] == 0x66 && imageBytes[5] == 0x74 && imageBytes[6] == 0x79 && imageBytes[7] == 0x70 &&
                       imageBytes[8] == 0x68 && imageBytes[9] == 0x65 && imageBytes[10] == 0x69 && imageBytes[11] == 0x63;

            if (mediaType.Contains("heif"))
                return imageBytes.Length >= 12 && imageBytes[4] == 0x66 && imageBytes[5] == 0x74 && imageBytes[6] == 0x79 && imageBytes[7] == 0x70 &&
                       imageBytes[8] == 0x68 && imageBytes[9] == 0x65 && imageBytes[10] == 0x69 && imageBytes[11] == 0x66;

            if (mediaType.Contains("avif"))
                return imageBytes.Length >= 12 && imageBytes[4] == 0x66 && imageBytes[5] == 0x74 && imageBytes[6] == 0x79 && imageBytes[7] == 0x70 &&
                       imageBytes[8] == 0x61 && imageBytes[9] == 0x76 && imageBytes[10] == 0x30 && imageBytes[11] == 0x31;

            // Si no se puede validar específicamente, asumir que es válido
            return true;
        }

        /// <summary>
        /// Crea el prompt personalizado exhaustivo para análisis detallado de espacios
        /// Incluye análisis de pisos, cortinas, muebles, calidad general, etc.
        /// </summary>
        private string CreateSimplePhotoAnalysisPrompt(MiCasaFotoAnalysisRequest analysisRequest)
        {
            // Construir descripción detallada de la sección con todos los datos disponibles
            var sectionDescription = new StringBuilder();
            sectionDescription.AppendLine($"TIPO DE SECCIÓN: {analysisRequest.TipoSeccion}");
            sectionDescription.AppendLine($"NOMBRE DE LA SECCIÓN: {analysisRequest.NombreSeccion}");
            sectionDescription.AppendLine($"Esta seccion esta ubicada en el piso : {analysisRequest.Piso}");
            sectionDescription.AppendLine($"Usa estos valores de las dimenciones de la seccion calcula dimenciones y verifica");

            if (analysisRequest.Dimensiones != null)
            {

                sectionDescription.AppendLine($"Dimencion Alto : {analysisRequest.Dimensiones.Alto}");
                sectionDescription.AppendLine($"Dimencion Ancho : {analysisRequest.Dimensiones.Ancho}");
                sectionDescription.AppendLine($"Dimencion Largo : {analysisRequest.Dimensiones.Largo}");
                sectionDescription.AppendLine($"Diametro: {analysisRequest.Dimensiones.Diametro}");
            }

             

            // Agregar propiedades/características si existen
            if (analysisRequest.Propiedades != null && analysisRequest.Propiedades.Count > 0)
            {
                sectionDescription.AppendLine("\nCARACTERÍSTICAS Y PROPIEDADES:");
                foreach (var propiedad in analysisRequest.Propiedades)
                {
                    if (!string.IsNullOrEmpty(propiedad.Valor))
                    {
                        sectionDescription.AppendLine($"  - {propiedad.Nombre} ({propiedad.Tipo}): {propiedad.Valor}");
                    }
                    else
                    {
                        sectionDescription.AppendLine($"  - {propiedad.Nombre} ({propiedad.Tipo})");
                    }
                }
            }

            // Agregar descripción de la foto si existe
            if (!string.IsNullOrEmpty(analysisRequest.Description))
            {
                sectionDescription.AppendLine($"\nDESCRIPCIÓN DE LA FOTO: {analysisRequest.Description}");
            }

            // Agregar observaciones especiales si existen
            if (!string.IsNullOrEmpty(analysisRequest.Observaciones))
            {
                sectionDescription.AppendLine($"\nOBSERVACIONES IMPORTANTES: {analysisRequest.Observaciones}");
            }

            // Agregar metadata adicional si existe
            if (analysisRequest.Metadata != null && analysisRequest.Metadata.Count > 0)
            {
                sectionDescription.AppendLine("\nINFORMACIÓN CONTEXTUAL ADICIONAL:");
                foreach (var metadata in analysisRequest.Metadata)
                {
                    sectionDescription.AppendLine($"  - {metadata.Key}: {metadata.Value}");
                }
            }

            var contextData = sectionDescription.ToString();

            // Construir el prompt de manera segura
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("ERES UN EXPERTO EN EVALUACIÓN DE ESPACIOS RESIDENCIALES PARA MICASA");
            promptBuilder.AppendLine("Tu tarea es realizar un análisis EXHAUSTIVO y DETALLADO de esta fotografía.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("INFORMACIÓN DE REFERENCIA DE LA SECCIÓN:");
            promptBuilder.AppendLine(contextData);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("DEBES ANALIZAR EN PROFUNDIDAD LOS SIGUIENTES ASPECTOS:");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("1. PISOS Y REVESTIMIENTOS:");
            promptBuilder.AppendLine("   - Tipo de piso (cerámica, madera, laminado, mármol, vinilo, etc.)");
            promptBuilder.AppendLine("   - Calidad: excelente/buena/regular/deficiente");
            promptBuilder.AppendLine("   - Estado: manchas, grietas, desgaste, limpieza");
            promptBuilder.AppendLine("   - Compatibilidad con contexto");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("2. ELEMENTOS DECORATIVOS:");
            promptBuilder.AppendLine("   - Cortinas/persianas: ¿hay? estado, estilo, color");
            promptBuilder.AppendLine("   - Muebles: presencia, tipo, estado de conservación");
            promptBuilder.AppendLine("   - Cuadros, espejos, adornos, plantas");
            promptBuilder.AppendLine("   - Iluminación: natural, artificial, tipo de lámparas");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("3. CONDICIONES GENERALES:");
            promptBuilder.AppendLine("   - Limpieza y orden del espacio");
            promptBuilder.AppendLine("   - Signos de mantenimiento o abandono");
            promptBuilder.AppendLine("   - Humedad, moho, problemas estructurales");
            promptBuilder.AppendLine("   - Amplitud y distribución del espacio");
            promptBuilder.AppendLine("   - Colores predominantes");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("4. FUNCIONALIDAD:");
            promptBuilder.AppendLine("   - ¿Parece funcional y habitable?");
            promptBuilder.AppendLine("   - Organización del mobiliario");
            promptBuilder.AppendLine("   - Accesibilidad y circulación");
            promptBuilder.AppendLine("   - Indicios de uso o desuso");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("5. CALIDAD GENERAL:");
            promptBuilder.AppendLine("   - Evaluación: Premium/Alta/Media/Baja");
            promptBuilder.AppendLine("   - Indicadores de inversión en mantenimiento");
            promptBuilder.AppendLine("   - Potencial de mejora y renovación");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("6. VALIDACIÓN CON CONTEXTO:");
            promptBuilder.AppendLine("   - ¿Coinciden las observaciones con el contexto?");
            promptBuilder.AppendLine("   - ¿Hay discrepancias importantes?");
            promptBuilder.AppendLine("   - ¿Las propiedades listadas se confirman?");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("7. DIMENSIONES Y SUPERFICIE:");
            promptBuilder.AppendLine("   - Estima el ANCHO del espacio en metros (de pared a pared)");
            promptBuilder.AppendLine("   - Estima el LARGO del espacio en metros (de pared a pared)");
            promptBuilder.AppendLine("   - Estima el ALTO del espacio en metros (altura del techo)");
            promptBuilder.AppendLine("   - Para objetos circulares: estima el DIÁMETRO en metros");
            promptBuilder.AppendLine("   - CALCULA LOS METROS CUADRADOS: ancho × largo");
            promptBuilder.AppendLine("   - Incluye observaciones sobre fiabilidad de estimaciones (ej: 'estimado desde ángulo' o 'visible claramente')");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("RESPONDE EN JSON VÁLIDO (sin ```json al inicio ni ``` al final):");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"descripcionGenerica\": \"Descripción breve máx 100 caracteres\",");
            promptBuilder.AppendLine("  \"elementosVisuales\": {");
            promptBuilder.AppendLine("    \"cantidadPersonas\": 0,");
            promptBuilder.AppendLine("    \"objetos\": [\"lista detallada de objetos\"],");
            promptBuilder.AppendLine("    \"escenario\": \"Descripción completa del entorno\",");
            promptBuilder.AppendLine("    \"caracteristicas\": [\"características observadas\"]");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"analisisPisos\": {");
            promptBuilder.AppendLine("    \"tipo\": \"Tipo de piso detectado\",");
            promptBuilder.AppendLine("    \"calidad\": \"excelente/buena/regular/deficiente\",");
            promptBuilder.AppendLine("    \"estado\": \"Descripción del estado\",");
            promptBuilder.AppendLine("    \"conservacion\": \"Nivel de conservación\"");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"elementosDecorativosAcabados\": {");
            promptBuilder.AppendLine("    \"cortinas\": \"Descripción de cortinas o No visible\",");
            promptBuilder.AppendLine("    \"muebles\": \"Descripción de muebles\",");
            promptBuilder.AppendLine("    \"iluminacion\": \"Tipos de iluminación\",");
            promptBuilder.AppendLine("    \"decoracion\": \"Otros elementos decorativos\",");
            promptBuilder.AppendLine("    \"plantas\": \"Presencia de plantas o elementos verdes\"");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"condicionesGenerales\": {");
            promptBuilder.AppendLine("    \"limpieza\": \"Nivel de limpieza y orden\",");
            promptBuilder.AppendLine("    \"mantenimiento\": \"Indicios de mantenimiento o abandono\",");
            promptBuilder.AppendLine("    \"problemasVisibles\": [\"lista de problemas o vacío\"],");
            promptBuilder.AppendLine("    \"amplitud\": \"Impresión de amplitud\",");
            promptBuilder.AppendLine("    \"coloresPredominantes\": [\"colores detectados\"]");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"funcionalidad\": {");
            promptBuilder.AppendLine("    \"habitabilidad\": \"sí/no/parcialmente\",");
            promptBuilder.AppendLine("    \"organizacion\": \"Evaluación de organización\",");
            promptBuilder.AppendLine("    \"potencialMejora\": \"Recomendaciones de mejora\"");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"calidadGeneral\": {");
            promptBuilder.AppendLine("    \"evaluacion\": \"Premium/Alta/Media/Baja\",");
            promptBuilder.AppendLine("    \"indicadoresCalidad\": [\"indicadores de inversión\"],");
            promptBuilder.AppendLine("    \"estado\": \"Descripción del estado general\"");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"validacionContexto\": {");
            promptBuilder.AppendLine("    \"coincidencias\": [\"aspectos que coinciden\"],");
            promptBuilder.AppendLine("    \"discrepancias\": [\"diferencias observadas\"],");
            promptBuilder.AppendLine("    \"observacionesAdicionales\": \"Comentarios de validación\"");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"dimensiones\": {");
            promptBuilder.AppendLine("    \"ancho\": 5.5,");
            promptBuilder.AppendLine("    \"largo\": 4.2,");
            promptBuilder.AppendLine("    \"alto\": 2.8,");
            promptBuilder.AppendLine("    \"diametro\": null,");
            promptBuilder.AppendLine("    \"observaciones\": \"Estimado desde ángulo, mínimamente visible en foto\"");
            promptBuilder.AppendLine("  },");
            promptBuilder.AppendLine("  \"metrosCuadrados\": 23.1,");
            promptBuilder.AppendLine("  \"datos\": [");
            promptBuilder.AppendLine("    {\"NombreDato\": \"Calidad de Piso\", \"Valor\": \"...\"}, ");
            promptBuilder.AppendLine("    {\"NombreDato\": \"Estado General\", \"Valor\": \"...\"},");
            promptBuilder.AppendLine("    {\"NombreDato\": \"Superficie (m²)\", \"Valor\": \"23.1\"}");
            promptBuilder.AppendLine("  ],");
            promptBuilder.AppendLine("  \"analisisDetallado\": \"Análisis exhaustivo del estado y condiciones\",");
            promptBuilder.AppendLine("  \"HTMLFullDescription\": \"<div style='font-family: Arial; padding: 20px; color: #2c3e50; background-color: #ecf0f1;'><h1>Análisis Completo</h1>... contenido HTML completo ...</div>\"");
            promptBuilder.AppendLine("}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("INSTRUCCIONES CRÍTICAS:");
            promptBuilder.AppendLine("✓ Usa TODOS los datos del contexto");
            promptBuilder.AppendLine("✓ Sé EXTREMADAMENTE detallado en cada sección");
            promptBuilder.AppendLine("✓ ESTIMA LAS DIMENSIONES con la mayor precisión posible");
            promptBuilder.AppendLine("✓ CALCULA LOS METROS CUADRADOS: ancho × largo");
            promptBuilder.AppendLine("✓ Describe TODO: pisos, cortinas, muebles, iluminación, estado");
            promptBuilder.AppendLine("✓ El JSON DEBE ser válido y completo");
            promptBuilder.AppendLine("✓ NO incluir ```json ni ```");
            promptBuilder.AppendLine("✓ Lenguaje profesional para evaluación de propiedades");
            promptBuilder.AppendLine("✓ Evalúa cada aspecto de forma objetiva");
            promptBuilder.AppendLine("✓ Incluye recomendaciones prácticas de mejora");

            return promptBuilder.ToString();
        }

        /// <summary>
        /// Detecta si el contenido descargado es HTML en lugar de una imagen
        /// </summary>
        private bool IsHtmlContent(byte[] data)
        {
            if (data == null || data.Length < 5)
                return false;

            // Buscar patrones comunes de HTML
            try
            {
                // Convertir primeros bytes a string para búsqueda rápida
                var preview = Encoding.UTF8.GetString(data.Take(1000).ToArray(), 0, Math.Min(1000, data.Length));
                
                // Patrones comunes de HTML/error pages
                if (preview.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                    preview.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
                    preview.Contains("<HTML", StringComparison.OrdinalIgnoreCase) ||
                    preview.Contains("<head", StringComparison.OrdinalIgnoreCase) ||
                    preview.Contains("401", StringComparison.OrdinalIgnoreCase) ||  // Unauthorized
                    preview.Contains("403", StringComparison.OrdinalIgnoreCase) ||  // Forbidden
                    preview.Contains("404", StringComparison.OrdinalIgnoreCase) ||  // Not Found
                    preview.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    preview.Contains("Access Denied", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Si hay error al decodificar, probablemente no sea HTML
                return false;
            }

            return false;
        }

    }

    /// <su 
}

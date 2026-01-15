using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwinAgentsLibrary.Models;
using TwinAgentsNetwork.Services;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente experto en MiCasa que analiza propiedades y perfiles de compradores
    /// para responder preguntas usando Azure OpenAI con soporte de conversación continua
    /// </summary>
    public class AgentMiCasaExpert
    {
        private readonly ILogger<AgentMiCasaExpert> _logger;
        private readonly IConfiguration _configuration;
        private readonly AzureOpenAIClient _azureOpenAIClient;
        private readonly ChatClient _chatClient;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _modelName;

        public AgentMiCasaExpert(ILogger<AgentMiCasaExpert> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            try
            {
                _endpoint = _configuration["Values:AzureOpenAI:Endpoint"] ?? 
                              _configuration["AzureOpenAI:Endpoint"] ?? "";
                _apiKey = _configuration["Values:AzureOpenAI:ApiKey"] ?? 
                            _configuration["AzureOpenAI:ApiKey"] ?? "";
                _modelName = _configuration["Values:AZURE_OPENAI_CHAT_MODEL"] ?? 
                               _configuration["AZURE_OPENAI_CHAT_MODEL"] ?? 
                               Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_MODEL") ?? "";
                
                if (string.IsNullOrEmpty(_endpoint) || string.IsNullOrEmpty(_apiKey))
                {
                    throw new InvalidOperationException("Azure OpenAI endpoint and API key are required. Configure in local.settings.json or environment variables.");
                }

                if (string.IsNullOrEmpty(_modelName))
                {
                    throw new InvalidOperationException("Azure OpenAI model name is required. Configure AZURE_OPENAI_CHAT_MODEL in local.settings.json or environment variables.");
                }

                _azureOpenAIClient = new AzureOpenAIClient(new Uri(_endpoint), new AzureKeyCredential(_apiKey));
                _chatClient = _azureOpenAIClient.GetChatClient(_modelName);

                _logger.LogInformation("✅ AgentMiCasaExpert initialized successfully with model: {ModelName}", _modelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize AgentMiCasaExpert");
                throw;
            }
        }

        /// <summary>
        /// Consulta al agente experto con información de propiedades y perfil del comprador
        /// Soporta continuidad de conversación usando SerializedThreadJson
        /// </summary>
        /// <param name="request">Solicitud con la lista de propiedades, ID del comprador y pregunta</param>
        /// <returns>Respuesta del agente experto con SerializedThreadJson para continuar la conversación</returns>
        public async Task<MiCasaExpertResult> ConsultarExpertoAsync(MiCasaExpertRequest request)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                // Validar entrada
                if (request == null)
                {
                    return new MiCasaExpertResult
                    {
                        Success = false,
                        ErrorMessage = "Request cannot be null"
                    };
                }

                if (string.IsNullOrEmpty(request.TwinId))
                {
                    return new MiCasaExpertResult
                    {
                        Success = false,
                        ErrorMessage = "TwinId is required"
                    };
                }

                if (string.IsNullOrEmpty(request.Pregunta))
                {
                    return new MiCasaExpertResult
                    {
                        Success = false,
                        ErrorMessage = "Pregunta is required"
                    };
                }

                if (request.PropiedadesIds == null || request.PropiedadesIds.Count == 0)
                {
                    return new MiCasaExpertResult
                    {
                        Success = false,
                        ErrorMessage = "At least one property is required"
                    };
                }

                _logger.LogInformation("🏠 Starting MiCasa Expert consultation for TwinId: {TwinId}, CompradorId: {CompradorId}, Properties: {PropertyCount}, HasPriorConversation: {HasThread}",
                    request.TwinId, request.CompradorMicrosoftOID ?? "N/A", request.PropiedadesIds.Count, !string.IsNullOrEmpty(request.SerializedThreadJson));

                // Paso 1: Obtener datos del comprador si se proporciona
                CompradorRequest? comprador = null;
                if (!string.IsNullOrEmpty(request.CompradorMicrosoftOID))
                {
                    _logger.LogInformation("📋 Fetching buyer profile...");
                    var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                    var compradorLogger = loggerFactory.CreateLogger<AentCustomerBuyerCosmosDB>();
                    var compradorService = new AentCustomerBuyerCosmosDB(compradorLogger, _configuration);

                    var compradorResult = await compradorService.GetCompradorByMicrosoftOIDAsync(
                        request.CompradorMicrosoftOID, request.TwinId);

                    if (compradorResult.Success && compradorResult.Comprador != null)
                    {
                        comprador = compradorResult.Comprador;
                        _logger.LogInformation("✅ Buyer profile retrieved: {Nombre} {Apellido}",
                            comprador.Nombre, comprador.Apellido);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Could not retrieve buyer profile: {Error}", compradorResult.ErrorMessage);
                    }
                }

                // Paso 2: Obtener datos de las propiedades
                _logger.LogInformation("🏡 Fetching property data...");
                var propiedadesConClientes = new List<ClienteConPropiedad>();
                var cosmosDb = new AgentTwinMiCasaCosmosDB(_configuration);

                foreach (var propiedadInfo in request.PropiedadesIds)
                {
                    try
                    {
                        var clientResult = await cosmosDb.GetClientByIdAsync(propiedadInfo.ClienteId);

                        if (clientResult.Success && clientResult.Client != null)
                        {
                            var cliente = clientResult.Client;
                            var propiedad = cliente.Propiedad?.FirstOrDefault(p => p.Id == propiedadInfo.PropiedadId);

                            if (propiedad != null)
                            {
                                propiedadesConClientes.Add(new ClienteConPropiedad
                                {
                                    Cliente = cliente,
                                    Propiedad = propiedad
                                });
                                _logger.LogInformation("✅ Property retrieved: {PropiedadId} from client {ClienteId}",
                                    propiedadInfo.PropiedadId, propiedadInfo.ClienteId);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Property {PropiedadId} not found in client {ClienteId}",
                                    propiedadInfo.PropiedadId, propiedadInfo.ClienteId);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Could not retrieve client {ClienteId}: {Error}",
                                propiedadInfo.ClienteId, clientResult.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error retrieving property {PropiedadId} from client {ClienteId}",
                            propiedadInfo.PropiedadId, propiedadInfo.ClienteId);
                    }
                }

                if (propiedadesConClientes.Count == 0)
                {
                    return new MiCasaExpertResult
                    {
                        Success = false,
                        ErrorMessage = "No properties could be retrieved from the provided IDs"
                    };
                }

                _logger.LogInformation("✅ Retrieved {Count} properties successfully", propiedadesConClientes.Count);

                // Paso 3: Crear el agente AI con instrucciones del sistema
                var systemInstructions = CreateSystemInstructions(comprador, propiedadesConClientes);

                _logger.LogInformation("🤖 Creating AI Agent with conversation memory...");

                // Crear el AIAgent usando el ChatClient convertido a IChatClient
                AIAgent agent = _chatClient.AsIChatClient().CreateAIAgent(
                    instructions: systemInstructions,
                    name: "MiCasaExpert");

                // Paso 4: Restaurar o crear nuevo thread para la conversación
                AgentThread thread;
                bool isNewConversation = string.IsNullOrEmpty(request.SerializedThreadJson);

                if (!isNewConversation)
                {
                    try
                    {
                        _logger.LogInformation("🔄 Restoring previous conversation thread...");
                        
                        // Limpiar el HTML del thread antes de deserializarlo para reducir tokens
                        string cleanedThreadJson = CleanThreadJsonForTokenReduction(request.SerializedThreadJson!);
                        _logger.LogInformation("🧹 Thread cleaned: Original size={OriginalSize}, Cleaned size={CleanedSize}",
                            request.SerializedThreadJson!.Length, cleanedThreadJson.Length);
                        
                        var reloaded = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(
                            cleanedThreadJson, 
                            JsonSerializerOptions.Web);
                        thread = agent.DeserializeThread(reloaded, JsonSerializerOptions.Web);
                        _logger.LogInformation("✅ Conversation thread restored successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Could not restore thread, starting new conversation");
                        thread = agent.GetNewThread();
                        isNewConversation = true;
                    }
                }
                else
                {
                    _logger.LogInformation("🆕 Starting new conversation thread...");
                    thread = agent.GetNewThread();
                }

                // Paso 5: Ejecutar la consulta con el agente
                _logger.LogInformation("💬 Sending user question to AI Agent...");

                // Si es nueva conversación, incluir contexto de propiedades en la primera pregunta
                string userMessage = isNewConversation 
                    ? $"{request.Pregunta}\n\n(Contexto adicional: {request.ContextoAdicional ?? "Ninguno"})"
                    : request.Pregunta;

                var agentResponse = await agent.RunAsync(userMessage, thread);

                if (agentResponse == null || string.IsNullOrEmpty(agentResponse.Text))
                {
                    throw new Exception("Empty response from AI Agent");
                }

                var aiResponse = agentResponse.Text;
                var processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                _logger.LogInformation("✅ Received response from AI Agent in {ProcessingTime}ms", processingTime);

                // Paso 6: Serializar el thread y limpiarlo para reducir tokens en futuras llamadas
                string serializedThread = thread.Serialize(JsonSerializerOptions.Web).GetRawText();
                string cleanedSerializedThread = CleanThreadJsonForTokenReduction(serializedThread);
                _logger.LogInformation("💾 Conversation thread serialized and cleaned for future continuity. Size reduced from {Original} to {Cleaned} bytes",
                    serializedThread.Length, cleanedSerializedThread.Length);

                // Parsear respuesta
                MiCasaExpertResult result;
                try
                {
                    // Intentar limpiar el JSON si viene con markdown
                    var cleanedResponse = aiResponse
                        .Replace("```json", "")
                        .Replace("```", "")
                        .Trim();
                    result = JsonConvert.DeserializeObject<MiCasaExpertResult>(cleanedResponse) ?? new MiCasaExpertResult();
                }
                catch
                {
                    // Si no es JSON válido, usar la respuesta como texto HTML
                    result = new MiCasaExpertResult
                    {
                        Respuesta = aiResponse
                    };
                }

                result.Success = true;
                result.ProcessingTimeMs = processingTime;
                result.PropiedadesAnalizadas = propiedadesConClientes.Count;
                result.TwinId = request.TwinId;
                result.SerializedThreadJson = cleanedSerializedThread;
                result.IsNewConversation = isNewConversation;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in MiCasa Expert consultation");
                return new MiCasaExpertResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                };
            }
        }

        #region Thread JSON Cleaning Methods

        /// <summary>
        /// Limpia el JSON del thread eliminando HTML y contenido innecesario para reducir tokens
        /// </summary>
        private string CleanThreadJsonForTokenReduction(string serializedThreadJson)
        {
            try
            {
                var threadElement = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(
                    serializedThreadJson, JsonSerializerOptions.Web);
                
                if (!threadElement.TryGetProperty("storeState", out var storeState)) 
                    return serializedThreadJson;
                if (!storeState.TryGetProperty("messages", out var messages)) 
                    return serializedThreadJson;

                var messageList = messages.EnumerateArray().ToList();
                var cleanedMessages = new List<JsonElement>();

                foreach (var msg in messageList)
                {
                    if (!msg.TryGetProperty("role", out var roleElement)) continue;
                    var role = roleElement.GetString();

                    var cleanMsg = new Dictionary<string, object?>();
                    
                    // Copiar propiedades básicas
                    if (msg.TryGetProperty("role", out var prop)) cleanMsg["role"] = role;
                    if (msg.TryGetProperty("authorName", out var authorProp)) cleanMsg["authorName"] = authorProp.GetString();
                    if (msg.TryGetProperty("createdAt", out var createdProp)) cleanMsg["createdAt"] = createdProp.GetString();
                    if (msg.TryGetProperty("messageId", out var msgIdProp)) cleanMsg["messageId"] = msgIdProp.GetString();

                    if (msg.TryGetProperty("contents", out var contentsProp))
                    {
                        var contentsList = new List<object>();
                        
                        foreach (var content in contentsProp.EnumerateArray())
                        {
                            if (content.TryGetProperty("text", out var textProp))
                            {
                                var originalText = textProp.GetString() ?? "";
                                string cleanedText = originalText;

                                if (role == "user")
                                {
                                    // Para mensajes de usuario, extraer solo la pregunta
                                    cleanedText = ExtractUserQuestion(originalText);
                                }
                                else if (role == "assistant")
                                {
                                    // Para respuestas del asistente, convertir HTML a texto plano
                                    cleanedText = ConvertHtmlToPlainText(originalText);
                                }

                                var contentObj = new Dictionary<string, object?>();
                                if (content.TryGetProperty("$type", out var typeProp)) 
                                    contentObj["$type"] = typeProp.GetString();
                                contentObj["text"] = cleanedText;
                                
                                contentsList.Add(contentObj);
                            }
                        }
                        
                        cleanMsg["contents"] = contentsList;
                    }

                    cleanedMessages.Add(System.Text.Json.JsonSerializer.SerializeToElement(cleanMsg, JsonSerializerOptions.Web));
                }

                var cleanedThread = new Dictionary<string, object>();
                var cleanedStoreState = new Dictionary<string, object>();
                cleanedStoreState["messages"] = cleanedMessages;
                cleanedThread["storeState"] = cleanedStoreState;

                return System.Text.Json.JsonSerializer.Serialize(cleanedThread, JsonSerializerOptions.Web);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error limpiando thread JSON. Retornando original.");
                return serializedThreadJson;
            }
        }

        /// <summary>
        /// Extrae solo la pregunta del usuario del mensaje completo
        /// </summary>
        private string ExtractUserQuestion(string userMessage)
        {
            // Si contiene el marcador de contexto adicional, extraer solo la pregunta
            if (userMessage.Contains("(Contexto adicional:"))
            {
                var idx = userMessage.IndexOf("(Contexto adicional:");
                if (idx > 0)
                {
                    return userMessage.Substring(0, idx).Trim();
                }
            }

            // Limitar longitud para reducir tokens
            if (userMessage.Length > 500)
            {
                return userMessage.Substring(0, 500) + "...";
            }

            return userMessage.Trim();
        }

        /// <summary>
        /// Convierte HTML a texto plano para reducir tokens
        /// </summary>
        private string ConvertHtmlToPlainText(string htmlContent)
        {
            // Si no contiene HTML, retornar como está
            if (!htmlContent.Contains("<") && !htmlContent.Contains(">"))
            {
                return htmlContent;
            }

            try
            {
                var text = htmlContent;
                
                // Remover DOCTYPE y XML declarations
                text = Regex.Replace(text, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"<\?xml[^>]*\?>", "", RegexOptions.IgnoreCase);
                
                // Remover scripts y estilos completamente
                text = Regex.Replace(text, @"<script[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                // Remover todas las etiquetas HTML
                text = Regex.Replace(text, @"<[^>]*>", " ");
                
                // Limpiar espacios múltiples
                text = Regex.Replace(text, @"\s+", " ");
                
                // Decodificar entidades HTML comunes
                text = text.Replace("&nbsp;", " ");
                text = text.Replace("&lt;", "<");
                text = text.Replace("&gt;", ">");
                text = text.Replace("&amp;", "&");
                text = text.Replace("&quot;", "\"");
                text = text.Replace("&#39;", "'");
                
                // Limitar longitud del texto resultante
                text = text.Trim();
                if (text.Length > 1000)
                {
                    text = text.Substring(0, 1000) + "...";
                }
                
                return text;
            }
            catch (Exception)
            {
                // Si hay error, retornar versión truncada del original
                return htmlContent.Length > 500 ? htmlContent.Substring(0, 500) + "..." : htmlContent;
            }
        }

        #endregion

        /// <summary>
        /// Crea las instrucciones del sistema para el agente
        /// </summary>
        private string CreateSystemInstructions(
            CompradorRequest? comprador,
            List<ClienteConPropiedad> propiedades)
        {
            var promptBuilder = new StringBuilder();

            promptBuilder.AppendLine("ERES UN EXPERTO ASESOR INMOBILIARIO PROFESIONAL DE MICASA.");
            promptBuilder.AppendLine("Tu rol es ayudar a compradores a encontrar la propiedad ideal analizando sus necesidades y las propiedades disponibles.");
            promptBuilder.AppendLine("MANTIENES MEMORIA DE LA CONVERSACIÓN - recuerda todo lo que el usuario te ha dicho anteriormente.");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("INSTRUCCIONES:");
            promptBuilder.AppendLine("✓ Sé profesional, amable y objetivo");
            promptBuilder.AppendLine("✓ Basa tus respuestas ÚNICAMENTE en la información proporcionada");
            promptBuilder.AppendLine("✓ NO inventes información que no se te haya dado");
            promptBuilder.AppendLine("✓ Proporciona recomendaciones prácticas y útiles");
            promptBuilder.AppendLine("✓ Responde en español");
            promptBuilder.AppendLine("✓ Usa el perfil del comprador para aprender que casa está buscando");
            promptBuilder.AppendLine("✓ Recuerda conversaciones anteriores y haz referencia a ellas cuando sea relevante");
            promptBuilder.AppendLine("✓ Trata de ser breve pero completo en tus respuestas");
            promptBuilder.AppendLine("✓ IMPORTANTE: Responde en HTML con colores, títulos, recomendación final etc.");
            promptBuilder.AppendLine("✓ No uses HTML con caracteres escapados (\\)");
            promptBuilder.AppendLine("✓ Llama al cliente por su nombre");
            promptBuilder.AppendLine("✓ Dile que eres un AI Agente experto en Bienes Raíces para México");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));

            // Información del comprador
            if (comprador != null)
            {
                promptBuilder.AppendLine("PERFIL DEL COMPRADOR:");
                promptBuilder.AppendLine("=".PadRight(80, '='));
                promptBuilder.AppendLine($"Nombre: {comprador.Nombre} {comprador.Apellido}");
                promptBuilder.AppendLine($"Email: {comprador.Email}");
                promptBuilder.AppendLine($"Teléfono: {comprador.Telefono}");
                promptBuilder.AppendLine($"Tipo de Cliente: {comprador.TipoCliente}");

                if (comprador.Presupuesto != null)
                {
                    promptBuilder.AppendLine();
                    promptBuilder.AppendLine("PRESUPUESTO:");
                    if (comprador.Presupuesto.PresupuestoMaximo.HasValue)
                        promptBuilder.AppendLine($"  - Presupuesto Máximo: ${comprador.Presupuesto.PresupuestoMaximo:N0} {comprador.Presupuesto.Moneda}");
                    if (!string.IsNullOrEmpty(comprador.Presupuesto.FormaPago))
                        promptBuilder.AppendLine($"  - Forma de Pago: {comprador.Presupuesto.FormaPago}");
                    if (comprador.Presupuesto.MontoCredito.HasValue && comprador.Presupuesto.MontoCredito > 0)
                        promptBuilder.AppendLine($"  - Monto Crédito: ${comprador.Presupuesto.MontoCredito:N0}");
                    if (comprador.Presupuesto.Enganche.HasValue && comprador.Presupuesto.Enganche > 0)
                        promptBuilder.AppendLine($"  - Enganche: ${comprador.Presupuesto.Enganche:N0}");
                }

                if (comprador.Ubicacion != null)
                {
                    promptBuilder.AppendLine();
                    promptBuilder.AppendLine("UBICACIÓN DESEADA:");
                    if (!string.IsNullOrEmpty(comprador.Ubicacion.Ciudad))
                        promptBuilder.AppendLine($"  - Ciudad: {comprador.Ubicacion.Ciudad}");
                    if (!string.IsNullOrEmpty(comprador.Ubicacion.Estado))
                        promptBuilder.AppendLine($"  - Estado: {comprador.Ubicacion.Estado}");
                    if (!string.IsNullOrEmpty(comprador.Ubicacion.Colonia))
                        promptBuilder.AppendLine($"  - Colonia: {comprador.Ubicacion.Colonia}");
                    if (comprador.Ubicacion.ZonasPreferidas?.Count > 0)
                        promptBuilder.AppendLine($"  - Zonas Preferidas: {string.Join(", ", comprador.Ubicacion.ZonasPreferidas)}");
                }

                if (comprador.Preferencias != null)
                {
                    promptBuilder.AppendLine();
                    promptBuilder.AppendLine("PREFERENCIAS DE PROPIEDAD:");
                    if (!string.IsNullOrEmpty(comprador.Preferencias.TipoPropiedad))
                        promptBuilder.AppendLine($"  - Tipo de Propiedad: {comprador.Preferencias.TipoPropiedad}");
                    if (!string.IsNullOrEmpty(comprador.Preferencias.TipoOperacion))
                        promptBuilder.AppendLine($"  - Tipo de Operación: {comprador.Preferencias.TipoOperacion}");
                    if (comprador.Preferencias.Recamaras.HasValue)
                        promptBuilder.AppendLine($"  - Recámaras: {comprador.Preferencias.Recamaras}");
                    if (comprador.Preferencias.Banos.HasValue)
                        promptBuilder.AppendLine($"  - Baños: {comprador.Preferencias.Banos}");
                    if (comprador.Preferencias.Estacionamientos.HasValue)
                        promptBuilder.AppendLine($"  - Estacionamientos: {comprador.Preferencias.Estacionamientos}");
                    if (comprador.Preferencias.MetrosConstruidos.HasValue)
                        promptBuilder.AppendLine($"  - Metros Construidos: {comprador.Preferencias.MetrosConstruidos} m²");
                    if (comprador.Preferencias.MetrosTerreno.HasValue)
                        promptBuilder.AppendLine($"  - Metros Terreno: {comprador.Preferencias.MetrosTerreno} m²");
                    if (comprador.Preferencias.Jardin == true)
                        promptBuilder.AppendLine("  - Requiere Jardín: Sí");
                    if (comprador.Preferencias.PetFriendly == true)
                        promptBuilder.AppendLine("  - Pet Friendly: Sí");
                    if (comprador.Preferencias.Amenidades?.Count > 0)
                        promptBuilder.AppendLine($"  - Amenidades Deseadas: {string.Join(", ", comprador.Preferencias.Amenidades)}");
                }

                if (!string.IsNullOrEmpty(comprador.Motivacion))
                    promptBuilder.AppendLine($"\nMotivación: {comprador.Motivacion}");
                if (!string.IsNullOrEmpty(comprador.TiempoCompra))
                    promptBuilder.AppendLine($"Tiempo para Compra: {comprador.TiempoCompra}");
                if (!string.IsNullOrEmpty(comprador.Notas))
                    promptBuilder.AppendLine($"Notas: {comprador.Notas}");

                promptBuilder.AppendLine();
            }

            // Información de las propiedades
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine($"PROPIEDADES DISPONIBLES ({propiedades.Count}):");
            promptBuilder.AppendLine("=".PadRight(80, '='));

            int propIndex = 1;
            foreach (var item in propiedades)
            {
                var cliente = item.Cliente;
                var propiedad = item.Propiedad;

                promptBuilder.AppendLine();
                promptBuilder.AppendLine($"--- PROPIEDAD #{propIndex} ---");
                promptBuilder.AppendLine($"ID: {propiedad.Id}");
                promptBuilder.AppendLine($"Vendedor: {cliente.NombreCliente} {cliente.ApellidoCliente}");
                promptBuilder.AppendLine($"Tipo: {propiedad.TipoPropiedad}");
                promptBuilder.AppendLine($"Operación: {propiedad.TipoOperacion}");
                promptBuilder.AppendLine($"Precio: ${propiedad.Precio:N0} {propiedad.Moneda}");

                if (propiedad.Direccion != null)
                {
                    var direccion = propiedad.Direccion.ObtenerDireccionCompleta();
                    if (!string.IsNullOrEmpty(direccion))
                        promptBuilder.AppendLine($"Dirección: {direccion}");
                    if (!string.IsNullOrEmpty(propiedad.Direccion.Colonia))
                        promptBuilder.AppendLine($"Colonia: {propiedad.Direccion.Colonia}");
                    if (!string.IsNullOrEmpty(propiedad.Direccion.Ciudad))
                        promptBuilder.AppendLine($"Ciudad: {propiedad.Direccion.Ciudad}");
                }

                if (propiedad.Caracteristicas != null)
                {
                    promptBuilder.AppendLine("Características:");
                    promptBuilder.AppendLine($"  - Metros Construidos: {propiedad.Caracteristicas.MetrosConstruidos} m²");
                    promptBuilder.AppendLine($"  - Metros Terreno: {propiedad.Caracteristicas.MetrosTerreno} m²");
                    promptBuilder.AppendLine($"  - Recámaras: {propiedad.Caracteristicas.NumRecamaras}");
                    promptBuilder.AppendLine($"  - Baños: {propiedad.Caracteristicas.NumBanos}");
                    promptBuilder.AppendLine($"  - Estacionamientos: {propiedad.Caracteristicas.NumEstacionamientos}");
                    if (propiedad.Caracteristicas.Antiguedad > 0)
                        promptBuilder.AppendLine($"  - Antigüedad: {propiedad.Caracteristicas.Antiguedad} años");
                }

                if (propiedad.Amenidades?.Count > 0)
                    promptBuilder.AppendLine($"Amenidades: {string.Join(", ", propiedad.Amenidades)}");

                if (!string.IsNullOrEmpty(propiedad.Descripcion))
                    promptBuilder.AppendLine($"Descripción: {propiedad.Descripcion}");

                if (!string.IsNullOrEmpty(propiedad.MotivoVenta))
                    promptBuilder.AppendLine($"Motivo de Venta: {propiedad.MotivoVenta}");

                if (!string.IsNullOrEmpty(propiedad.Urgencia))
                    promptBuilder.AppendLine($"Urgencia: {propiedad.Urgencia}");

                if (!string.IsNullOrEmpty(propiedad.Disponibilidad))
                    promptBuilder.AppendLine($"Disponibilidad: {propiedad.Disponibilidad}");

                if (!string.IsNullOrEmpty(propiedad.Estatus))
                    promptBuilder.AppendLine($"Estatus: {propiedad.Estatus}");

                // Incluir análisis si existe
                if (propiedad.AnalysisResult != null && propiedad.AnalysisResult.Success)
                {
                    promptBuilder.AppendLine();
                    promptBuilder.AppendLine("Análisis de la Propiedad:");
                    if (!string.IsNullOrEmpty(propiedad.AnalysisResult.SumarioEjecutivo))
                        promptBuilder.AppendLine($"  Sumario: {propiedad.AnalysisResult.SumarioEjecutivo}");
                    if (propiedad.AnalysisResult.TotalMetrosCuadrados.HasValue)
                        promptBuilder.AppendLine($"  Metros Cuadrados (análisis): {propiedad.AnalysisResult.TotalMetrosCuadrados} m²");
                    if (propiedad.AnalysisResult.TotalCuartos.HasValue)
                        promptBuilder.AppendLine($"  Total Cuartos (análisis): {propiedad.AnalysisResult.TotalCuartos}");
                    if (propiedad.AnalysisResult.Recomendaciones?.Count > 0)
                        promptBuilder.AppendLine($"  Recomendaciones: {string.Join("; ", propiedad.AnalysisResult.Recomendaciones)}");
                }

                propIndex++;
            }

            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("FORMATO DE RESPUESTA (JSON válido sin ```json ni ```):");
            promptBuilder.AppendLine("{");
            promptBuilder.AppendLine("  \"respuesta\": \"Tu respuesta completa y detallada aquí en formato HTML con colores profesional...\",");
            promptBuilder.AppendLine("  \"propiedadesRecomendadas\": [\"ID de propiedades recomendadas si aplica\"],");
            promptBuilder.AppendLine("  \"observaciones\": [\"Observaciones importantes\"],");
            promptBuilder.AppendLine("  \"siguientesPasos\": [\"Pasos sugeridos para el comprador\"]");
            promptBuilder.AppendLine("}");

            return promptBuilder.ToString();
        }

        /// <summary>
        /// Crea el prompt para el agente experto (método legacy para compatibilidad)
        /// </summary>
        private string CreateExpertPrompt(
            CompradorRequest? comprador,
            List<ClienteConPropiedad> propiedades,
            string pregunta,
            string? contextoAdicional)
        {
            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine(CreateSystemInstructions(comprador, propiedades));

            // Contexto adicional
            if (!string.IsNullOrEmpty(contextoAdicional))
            {
                promptBuilder.AppendLine();
                promptBuilder.AppendLine("=".PadRight(80, '='));
                promptBuilder.AppendLine("CONTEXTO ADICIONAL:");
                promptBuilder.AppendLine("=".PadRight(80, '='));
                promptBuilder.AppendLine(contextoAdicional);
            }

            // La pregunta del usuario
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine("PREGUNTA DEL USUARIO:");
            promptBuilder.AppendLine("=".PadRight(80, '='));
            promptBuilder.AppendLine(pregunta);

            return promptBuilder.ToString();
        }
    }

    #region Request/Response Models

    /// <summary>
    /// Solicitud para consultar al agente experto MiCasa
    /// </summary>
    public class MiCasaExpertRequest
    {
        /// <summary>
        /// ID del Twin
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// Microsoft OID del comprador (opcional - para obtener su perfil)
        /// </summary>
        public string? CompradorMicrosoftOID { get; set; }

        /// <summary>
        /// Lista de propiedades a analizar (ClienteId + PropiedadId)
        /// </summary>
        public List<PropiedadIdentificador> PropiedadesIds { get; set; } = new();

        /// <summary>
        /// Pregunta del usuario para el agente experto
        /// </summary>
        public string Pregunta { get; set; } = string.Empty;

        /// <summary>
        /// Contexto adicional para la consulta (opcional)
        /// </summary>
        public string? ContextoAdicional { get; set; }

        /// <summary>
        /// JSON serializado del thread de conversación anterior (opcional - para continuar conversación)
        /// </summary>
        public string? SerializedThreadJson { get; set; }
    }

    /// <summary>
    /// Identificador de una propiedad (ClienteId + PropiedadId)
    /// </summary>
    public class PropiedadIdentificador
    {
        /// <summary>
        /// ID del cliente vendedor (documento en Cosmos DB)
        /// </summary>
        public string ClienteId { get; set; } = string.Empty;

        /// <summary>
        /// ID de la propiedad dentro del array de propiedades del cliente
        /// </summary>
        public string PropiedadId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Resultado de la consulta al agente experto
    /// </summary>
    public class MiCasaExpertResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public double ProcessingTimeMs { get; set; }
        public string? TwinId { get; set; }
        public int PropiedadesAnalizadas { get; set; }

        /// <summary>
        /// Indica si es una nueva conversación o continuación de una existente
        /// </summary>
        public bool IsNewConversation { get; set; }

        /// <summary>
        /// JSON serializado del thread de conversación para continuar en futuras llamadas
        /// </summary>
        public string? SerializedThreadJson { get; set; }

        [JsonProperty("respuesta")]
        public string Respuesta { get; set; } = string.Empty;

        [JsonProperty("propiedadesRecomendadas")]
        public List<string>? PropiedadesRecomendadas { get; set; }

        [JsonProperty("observaciones")]
        public List<string>? Observaciones { get; set; }

        [JsonProperty("siguientesPasos")]
        public List<string>? SiguientesPasos { get; set; }
    }

    /// <summary>
    /// Clase auxiliar para agrupar cliente con propiedad
    /// </summary>
    internal class ClienteConPropiedad
    {
        public MiCasaClientes Cliente { get; set; } = null!;
        public Propiedad Propiedad { get; set; } = null!;
    }

    #endregion
}

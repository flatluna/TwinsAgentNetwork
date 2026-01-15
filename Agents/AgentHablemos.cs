using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using TwinAgentsNetwork.Services;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.IO;
using System.Text.Json;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// Agente de mensajería simple 1-a-1 sin IA
    /// Usa Cosmos DB para persistir conversaciones entre pares de usuarios
    /// Container: twinhablemos
    /// Partition Key: /PairId (combinación de los dos TwinIDs)
    /// </summary>
    public class AgentHablemos
    {
        private readonly ILogger<AgentHablemos> _logger;
        private readonly IConfiguration _configuration;
        private readonly AgentHablemosCosmosDB _cosmosService;

        public AgentHablemos(ILogger<AgentHablemos> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Inicializar servicio de Cosmos DB
            var cosmosLogger = LoggerFactory.Create(builder => builder.AddConsole())
                .CreateLogger<AgentHablemosCosmosDB>();
            _cosmosService = new AgentHablemosCosmosDB(cosmosLogger, configuration);
            
            _logger.LogInformation("?? AgentHablemos initialized - Simple 1-to-1 messaging");
        }

        /// <summary>
        /// Envía un mensaje entre dos usuarios y lo guarda automáticamente en Cosmos DB
        /// Busca primero si existe la conversación antes de crear una nueva
        /// </summary>
        /// <param name="request">Datos del mensaje a enviar</param>
        /// <returns>Resultado con el mensaje enviado y guardado</returns>
        public async Task<EnviarMensajeResult> EnviarMensajeAsync(EnviarMensajeRequest request)
        {
            try
            {
                // Validar datos requeridos
                if (string.IsNullOrEmpty(request.ClientePrimeroID) || 
                    string.IsNullOrEmpty(request.ClienteSegundoID))
                {
                    return new EnviarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = "Los IDs de ambos clientes son requeridos"
                    };
                }

                if (string.IsNullOrEmpty(request.DuenoAppTwinID) || 
                    string.IsNullOrEmpty(request.DuenoAppMicrosoftOID))
                {
                    return new EnviarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = "Los datos del dueño de la app son requeridos"
                    };
                }

                if (string.IsNullOrEmpty(request.Mensaje))
                {
                    return new EnviarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = "El mensaje no puede estar vacío"
                    };
                }

                if (string.IsNullOrEmpty(request.DeQuien) || string.IsNullOrEmpty(request.ParaQuien))
                {
                    return new EnviarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = "Debe especificar quién envía y quién recibe"
                    };
                }

                // ?? Generar el ID del documento según el Origin
                string documentId;
                if (request.Origin == "AgenteInmobiliario")
                {
                    // Para agente: primero cliente, segundo agente
                    documentId = $"{request.ClientePrimeroID}_{request.ClienteSegundoID}";
                    _logger.LogInformation("?? ID generado para AgenteInmobiliario: {DocumentId}", documentId);
                }
                else if (request.Origin == "Cliente")
                {
                    // Para cliente: primero agente, segundo cliente (invertido)
                    documentId = $"{request.ClienteSegundoID}_{request.ClientePrimeroID}";
                    _logger.LogInformation("?? ID generado para Cliente: {DocumentId}", documentId);
                }
                else
                {
                    // Default: orden alfabético (comportamiento original)
                    documentId = GeneratePairId(request.ClientePrimeroID, request.ClienteSegundoID);
                    _logger.LogInformation("?? ID generado (default): {DocumentId}", documentId);
                }

                _logger.LogInformation("?? Enviando mensaje de {From} para {To}. Origin: {Origin}, DocumentID: {DocId}", 
                    request.DeQuien, request.ParaQuien, request.Origin, documentId);

                // ?? Primero buscar si ya existe la conversación
                var existingConversation = await _cosmosService.ObtenerConversacionPorIdAsync(
                    documentId
                    
                );

                // Crear el mensaje
                var mensaje = new HablemosMessage
                {
                    MessageId = string.IsNullOrEmpty(request.MessageId) 
                        ? Guid.NewGuid().ToString() 
                        : request.MessageId,
                    ClientePrimeroID = request.ClientePrimeroID, 
                    ClienteSegundoID = request.ClienteSegundoID, 
                    TwinID = request.DuenoAppMicrosoftOID,
                    DuenoAppMicrosoftOID = request.DuenoAppMicrosoftOID,
                    Mensaje = request.Mensaje,
                    DeQuien = request.DeQuien,
                    ParaQuien = request.ParaQuien,
                    FechaCreado = DateTime.Now,
                    Hora = DateTime.Now.ToString("HH:mm:ss"),
                    IsRead = false,
                    IsDelivered = false,
                    // ??? Campos de audio de voz
                    VozNombreArchivo = request.vozNombreArchivo ?? string.Empty,
                    vozPath = request.VozPath ?? string.Empty,
                    ClientID = request.ClientID ?? string.Empty,
                    SASURLVOZ = request.SASURLVOZ ?? string.Empty
                  
                };

                _logger.LogInformation("? Mensaje creado: {MessageId}", mensaje.MessageId);

                HablemosConversation conversation;

                if (existingConversation != null)
                {
                    // ?? Conversación existente - agregar mensaje
                    _logger.LogInformation("?? Conversación existente encontrada. Agregando mensaje...");
                    conversation = existingConversation;
                    conversation.Mensajes.Add(mensaje);
                    conversation.LastActivityAt = DateTime.UtcNow;
                }
                else
                {
                    // ?? Nueva conversación
                    _logger.LogInformation("?? Creando nueva conversación con ID: {DocumentId}", documentId);
                    conversation = new HablemosConversation
                    {
                        Id = documentId,
                        PairId = documentId,
                        ClientePrimeroID = request.ClientePrimeroID,
                        ClienteSegundoID = request.ClienteSegundoID,
                        TwinID = request.DuenoAppMicrosoftOID,
                        DuenoAppMicrosoftOID = request.DuenoAppMicrosoftOID,
                        Mensajes = new List<HablemosMessage> { mensaje },
                        CreatedAt = DateTime.UtcNow,
                        LastActivityAt = DateTime.UtcNow,
                        // ??? Campos de audio de voz a nivel de conversación
                        VosPath = request.VozPath ?? string.Empty,
                        vozNombreArchivo = request.vozNombreArchivo ?? string.Empty, 
                        ClientID = request.ClientID ?? string.Empty
                    };
                }

                // ?? Guardar (UpdateInsert) en Cosmos DB
                mensaje.IsDelivered = true;
                mensaje.DeliveredAt = DateTime.UtcNow;

                var saveResult = await _cosmosService.GuardarConversacionAsync(
                    conversation, 
                    request.DuenoAppMicrosoftOID
                );

                if (!saveResult.Success)
                {
                    _logger.LogError("? Error guardando en Cosmos DB: {Error}", saveResult.ErrorMessage);
                    return new EnviarMensajeResult
                    {
                        Success = false,
                        ErrorMessage = $"Error guardando en Cosmos DB: {saveResult.ErrorMessage}",
                        Mensaje = mensaje
                    };
                }

                _logger.LogInformation("?? Conversación guardada en Cosmos DB. DocumentID: {DocId}, RU: {RU}",
                    documentId, saveResult.RUConsumed);

                return new EnviarMensajeResult
                {
                    Success = true,
                    Mensaje = mensaje,
                    PairId = documentId,
                    RUConsumed = saveResult.RUConsumed,
                    Message = "Mensaje enviado y guardado exitosamente"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error enviando mensaje");
                return new EnviarMensajeResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtiene mensajes entre dos usuarios filtrados por período
        /// MEJORADO: Ahora consulta directamente desde Cosmos DB
        /// </summary>
        public async Task<GetMensajesResult> GetMensajesAsync(
            string clientePrimeroID,
            string clienteSegundoID,
            string twinID,
            string periodo = "dia")
        {
            try
            {
                if (string.IsNullOrEmpty(clientePrimeroID) || string.IsNullOrEmpty(clienteSegundoID))
                {
                    return new GetMensajesResult
                    {
                        Success = false,
                        ErrorMessage = "Los IDs de ambos clientes son requeridos",
                        Mensajes = new List<HablemosMessage>()
                    };
                }

                if (string.IsNullOrEmpty(twinID))
                {
                    return new GetMensajesResult
                    {
                        Success = false,
                        ErrorMessage = "TwinID es requerido",
                        Mensajes = new List<HablemosMessage>()
                    };
                }

                _logger.LogInformation("?? Obteniendo mensajes por {Periodo} entre {Client1} y {Client2}",
                    periodo, clientePrimeroID, clienteSegundoID);

                // Calcular fecha de inicio según el período
                DateTime fechaInicio = periodo.ToLower() switch
                {
                    "semana" => DateTime.UtcNow.AddDays(-7),
                    "mes" => DateTime.UtcNow.AddDays(-30),
                    _ => DateTime.UtcNow.Date // dia (default)
                };

                DateTime fechaFin = DateTime.UtcNow;

                _logger.LogInformation("?? Filtrando mensajes desde: {FechaInicio} hasta {FechaFin}", 
                    fechaInicio, fechaFin);

                // ?? Obtener desde Cosmos DB con TwinID
                var cosmosResult = await _cosmosService.ObtenerMensajesAsync(
                    clientePrimeroID,
                    clienteSegundoID,
                    twinID,
                    fechaInicio,
                    fechaFin
                );

                if (!cosmosResult.Success)
                {
                    return new GetMensajesResult
                    {
                        Success = false,
                        ErrorMessage = cosmosResult.ErrorMessage,
                        Mensajes = new List<HablemosMessage>()
                    };
                }

                return new GetMensajesResult
                {
                    Success = true,
                    Mensajes = cosmosResult.Mensajes,
                    Periodo = periodo,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    TotalMensajes = cosmosResult.TotalMensajes,
                    RUConsumed = cosmosResult.RUConsumed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error obteniendo mensajes");
                return new GetMensajesResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Mensajes = new List<HablemosMessage>()
                };
            }
        }

        /// <summary>
        /// Marca mensajes como leídos en Cosmos DB
        /// MEJORADO: Ahora actualiza directamente en Cosmos DB
        /// </summary>
        public async Task<HablemosMarkAsReadResult> MarcarComoLeidoAsync(
            string clientePrimeroID,
            string clienteSegundoID,
            string twinID,
            List<string> messageIds, 
            string leidoPor)
        {
            try
            {
                if (messageIds == null || messageIds.Count == 0)
                {
                    return new HablemosMarkAsReadResult
                    {
                        Success = false,
                        ErrorMessage = "Debe proporcionar al menos un messageId"
                    };
                }

                if (string.IsNullOrEmpty(leidoPor))
                {
                    return new HablemosMarkAsReadResult
                    {
                        Success = false,
                        ErrorMessage = "Debe especificar quién leyó los mensajes"
                    };
                }

                if (string.IsNullOrEmpty(twinID))
                {
                    return new HablemosMarkAsReadResult
                    {
                        Success = false,
                        ErrorMessage = "TwinID es requerido"
                    };
                }

                _logger.LogInformation("? Marcando {Count} mensajes como leídos por {User}",
                    messageIds.Count, leidoPor);

                // ?? Generar PairId y actualizar en Cosmos DB con TwinID
                string pairId = GeneratePairId(clientePrimeroID, clienteSegundoID);
                
                var cosmosResult = await _cosmosService.MarcarMensajesComoLeidosAsync(
                    pairId,
                    twinID,
                    messageIds,
                    leidoPor
                );

                if (!cosmosResult.Success)
                {
                    return new HablemosMarkAsReadResult
                    {
                        Success = false,
                        ErrorMessage = cosmosResult.ErrorMessage
                    };
                }

                return new HablemosMarkAsReadResult
                {
                    Success = true,
                    MarcadosCount = cosmosResult.MarcadosCount,
                    RUConsumed = cosmosResult.RUConsumed,
                    Message = $"{cosmosResult.MarcadosCount} mensajes marcados como leídos"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error marcando mensajes como leídos");
                return new HablemosMarkAsReadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Obtiene todas las conversaciones de un usuario desde Cosmos DB
        /// NUEVO: Consulta directa a Cosmos DB con TwinID y ambas combinaciones de PairId
        /// </summary>
        public async Task<ObtenerConversacionesUsuarioResult> ObtenerConversacionesUsuarioAsync(
            string clienteID,
            string agenteID, 
            string twinID)
        {
            try
            {
                if (string.IsNullOrEmpty(clienteID))
                {
                    return new ObtenerConversacionesUsuarioResult
                    {
                        Success = false,
                        ErrorMessage = "ClienteID es requerido"
                    };
                }

                if (string.IsNullOrEmpty(agenteID))
                {
                    return new ObtenerConversacionesUsuarioResult
                    {
                        Success = false,
                        ErrorMessage = "AgenteID es requerido"
                    };
                }

                if (string.IsNullOrEmpty(twinID))
                {
                    return new ObtenerConversacionesUsuarioResult
                    {
                        Success = false,
                        ErrorMessage = "TwinID es requerido"
                    };
                }

                _logger.LogInformation("?? Obteniendo conversaciones. ClienteID: {ClienteID}, AgenteID: {AgenteID}, TwinID: {TwinID}", 
                    clienteID, agenteID, twinID);

                var cosmosResult = await _cosmosService.ObtenerConversacionesUsuarioAsync(clienteID, agenteID, twinID);

                if (!cosmosResult.Success)
                {
                    return new ObtenerConversacionesUsuarioResult
                    {
                        Success = false,
                        ErrorMessage = cosmosResult.ErrorMessage
                    };
                }

                return new ObtenerConversacionesUsuarioResult
                {
                    Success = true,
                    Conversaciones = cosmosResult.Conversaciones,
                    TotalConversaciones = cosmosResult.TotalConversaciones,
                    RUConsumed = cosmosResult.RUConsumed
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error obteniendo conversaciones");
                return new ObtenerConversacionesUsuarioResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Genera el PairId para identificar la conversación entre dos usuarios
        /// El PairId siempre es el mismo independientemente del orden de los usuarios
        /// </summary>
        public static string GeneratePairId(string twinId1, string twinId2)
        {
            // Ordenar alfabéticamente para que siempre sea el mismo
            var ids = new[] { twinId1, twinId2 };
            Array.Sort(ids);
            return $"{ids[0]}_{ids[1]}";
        }

        /// <summary>
        /// Mejora un texto usando IA según el estilo especificado
        /// Estilos disponibles: conciso, sumario, formal, casual, profesional, creativo
        /// </summary>
        public async Task<MejorarTextoResult> MejorarTextoConEstiloAsync(
            string textoOriginal,
            string estilo = "conciso")
        {
            try
            {
                if (string.IsNullOrEmpty(textoOriginal))
                {
                    return new MejorarTextoResult
                    {
                        Success = false,
                        ErrorMessage = "El texto original no puede estar vacío"
                    };
                }

                if (string.IsNullOrEmpty(estilo))
                {
                    estilo = "conciso";
                }

                // Validate input for security
                if (ContainsBadWords(textoOriginal) || ContainsPromptInjection(textoOriginal))
                {
                    return new MejorarTextoResult
                    {
                        Success = false,
                        ErrorMessage = "Texto contiene contenido no permitido o intento de inyección. Por favor reformula el texto.",
                        TextoOriginal = textoOriginal,
                        TextoMejorado = textoOriginal,
                        EstiloAplicado = estilo
                    };
                }

                _logger.LogInformation("?? Mejorando texto con estilo: {Estilo}", estilo);

                // Obtener configuración de Azure OpenAI
                string azureOpenAIEndpoint = _configuration["AZURE_OPENAI_ENDPOINT"] 
                    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
                    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not configured.");

                string azureOpenAIModelName = _configuration["AZURE_OPENAI_MODEL_NAME"] 
                    ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL_NAME") 
                    ?? throw new InvalidOperationException("AZURE_OPENAI_MODEL_NAME is not configured.");

                // Crear instrucciones específicas según el estilo
                string instrucciones = CrearInstruccionesSegunEstilo(estilo);

                // Crear el agente de IA usando el patrón correcto con AsIChatClient
                var chatClient = new AzureOpenAIClient(
                    new Uri(azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(azureOpenAIModelName);

                AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: instrucciones,
                    name: "AsistenteEscritura");

                AgentThread thread = agent.GetNewThread();

                // Crear el prompt con el texto a mejorar
                string prompt = $@"
Por favor, mejora el siguiente texto aplicando el estilo '{estilo}':

TEXTO ORIGINAL:
{textoOriginal}

INSTRUCCIONES:
- Mantén el significado y mensaje principal
- Aplica el estilo {estilo} de manera efectiva
- Retorna SOLO el texto mejorado, sin explicaciones adicionales
- Responde en el mismo idioma del texto original
";

                _logger.LogInformation("?? Enviando texto a IA para mejora con estilo {Estilo}", estilo);

                // Ejecutar el agente
                var response = await agent.RunAsync(prompt, thread);
                
                // Obtener el último mensaje de respuesta
                string textoMejorado = response.Text;

                if (string.IsNullOrEmpty(textoMejorado))
                {
                    return new MejorarTextoResult
                    {
                        Success = false,
                        ErrorMessage = "No se recibió respuesta válida de la IA"
                    };
                }

                _logger.LogInformation("? Texto mejorado exitosamente. Original: {OrigLen} chars, Mejorado: {MejLen} chars",
                    textoOriginal.Length, textoMejorado.Length);

                // Serializar el thread por si se quiere continuar la conversación
                string serializedThread = thread.Serialize(System.Text.Json.JsonSerializerOptions.Web).GetRawText();

                return new MejorarTextoResult
                {
                    Success = true,
                    TextoOriginal = textoOriginal,
                    TextoMejorado = textoMejorado,
                    EstiloAplicado = estilo,
                    SerializedThread = serializedThread,
                    CaracteresOriginales = textoOriginal.Length,
                    CaracteresMejorados = textoMejorado.Length
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error mejorando texto con IA");
                return new MejorarTextoResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Validates input for inappropriate content
        /// </summary>
        private bool ContainsBadWords(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;

            // Basic bad words filter - expand as needed
            string[] badWords = { "spam", "hack", "malware", "virus", "exploit" };
            string lowerInput = input.ToLower();

            return badWords.Any(word => lowerInput.Contains(word));
        }

        /// <summary>
        /// Validates input for prompt injection attempts
        /// </summary>
        private bool ContainsPromptInjection(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;

            string lowerInput = input.ToLower();
            string[] injectionPatterns =
            {
                "ignore previous", "forget everything", "system prompt", "override",
                "admin access", "root access", "bypass", "jailbreak", "disable safety"
            };

            return injectionPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        /// <summary>
        /// Crea las instrucciones específicas para cada estilo de escritura
        /// </summary>
        private string CrearInstruccionesSegunEstilo(string estilo)
        {
            return estilo.ToLower() switch
            {
                "conciso" => @"
?? IDENTIDAD: Eres un EXPERTO EN ESCRITURA CONCISA Y DIRECTA.

?? TU ESPECIALIZACIÓN:
- Eliminar palabras innecesarias y redundancias
- Ir directo al punto sin perder el mensaje
- Usar frases cortas y claras
- Mantener solo la información esencial

?? REQUISITOS:
- Reducir el texto sin perder el significado
- Usar verbos de acción
- Eliminar adjetivos superfluos
- Ser claro y directo

? OBJETIVO: Texto claro, breve y al punto.",

                "sumario" => @"
?? IDENTIDAD: Eres un EXPERTO EN CREAR RESÚMENES EJECUTIVOS.

?? TU ESPECIALIZACIÓN:
- Extraer los puntos clave del texto
- Sintetizar información compleja
- Presentar ideas principales en orden lógico
- Omitir detalles secundarios

?? REQUISITOS:
- Capturar la esencia del mensaje
- Usar bullets o lista numerada si es apropiado
- Mantener coherencia y claridad
- Reducir significativamente la longitud

? OBJETIVO: Resumen claro de los puntos principales.",

                "formal" => @"
?? IDENTIDAD: Eres un EXPERTO EN COMUNICACIÓN FORMAL Y PROFESIONAL.

?? TU ESPECIALIZACIÓN:
- Usar lenguaje formal y profesional
- Estructurar textos con etiqueta corporativa
- Emplear vocabulario técnico apropiado
- Mantener tono respetuoso y cortés

?? REQUISITOS:
- Usar tercera persona cuando sea apropiado
- Evitar contracciones y coloquialismos
- Usar conectores formales
- Mantener estructura profesional

? OBJETIVO: Texto formal apropiado para contextos corporativos.",

                "casual" => @"
?? IDENTIDAD: Eres un EXPERTO EN COMUNICACIÓN CASUAL Y AMIGABLE.

?? TU ESPECIALIZACIÓN:
- Usar lenguaje coloquial y cercano
- Crear conexión con el lector
- Emplear tono conversacional
- Hacer el texto más accesible

?? REQUISITOS:
- Usar primera o segunda persona
- Permitir contracciones naturales
- Usar expresiones cotidianas
- Mantener calidez y cercanía

? OBJETIVO: Texto amigable y fácil de leer.",

                "profesional" => @"
?? IDENTIDAD: Eres un EXPERTO EN COMUNICACIÓN PROFESIONAL DE NEGOCIOS.

?? TU ESPECIALIZACIÓN:
- Equilibrar formalidad con accesibilidad
- Usar terminología de negocios apropiada
- Estructurar información de manera lógica
- Proyectar credibilidad y expertise

?? REQUISITOS:
- Usar lenguaje claro pero profesional
- Incluir datos o ejemplos si mejora el mensaje
- Mantener tono confiable
- Ser persuasivo cuando sea apropiado

? OBJETIVO: Texto profesional que inspira confianza.",

                "creativo" => @"
?? IDENTIDAD: Eres un EXPERTO EN ESCRITURA CREATIVA Y ATRACTIVA.

?? TU ESPECIALIZACIÓN:
- Usar metáforas y analogías efectivas
- Crear narrativas interesantes
- Emplear lenguaje descriptivo y vívido
- Capturar la atención del lector

?? REQUISITOS:
- Usar vocabulario rico y variado
- Incluir elementos narrativos
- Crear imágenes mentales
- Mantener engagement del lector

? OBJETIVO: Texto creativo que cautiva y entretiene.",

                _ => $"?? IDENTIDAD: Eres un EXPERTO EN MEJORAR TEXTOS.\n\n" +
                      "?? TU ESPECIALIZACIÓN:\n" +
                      "- Mejorar claridad y fluidez\n" +
                      "- Corregir errores gramaticales\n" +
                      "- Optimizar estructura\n" +
                      "- Mantener el mensaje original\n\n" +
                      "?? REQUISITOS:\n" +
                      "- Respetar el tono original\n" +
                      "- Mejorar legibilidad\n" +
                      "- Mantener coherencia\n" +
                      "- Preservar el significado\n\n" +
                      $"? OBJETIVO: Texto mejorado con estilo '{estilo}'."
            };
        }
    }

    #region Data Models

    /// <summary>
    /// Request para enviar un mensaje
    /// </summary>
    public class EnviarMensajeRequest
    {
        public string ClientePrimeroID { get; set; } = string.Empty;
        
        public string ClienteSegundoID { get; set; } = string.Empty; 
        public string DuenoAppTwinID { get; set; } = string.Empty;
        public string DuenoAppMicrosoftOID { get; set; } = string.Empty;
        public string Mensaje { get; set; } = string.Empty;
        public string DeQuien { get; set; } = string.Empty;  // TwinID de quien envía
        public string ParaQuien { get; set; } = string.Empty;  // TwinID de quien recibe

        public string Origin { get; set; } = string.Empty;  // Opcional: origen del mensaje (app, web, etc.)

        public string? MessageId { get; set; }  // Opcional: si se quiere especificar un ID

        // ??? Campos de audio de voz
        [JsonProperty("vozPath")]
        public string? VozPath { get; set; }

        [JsonProperty("vozNombreArchivo")]
        public string? vozNombreArchivo { get; set; }

        [JsonProperty("ClientID")]
        public string? ClientID { get; set; }

        [JsonProperty("SASURLVOZ")]
        public string? SASURLVOZ { get; set; }
    }

    /// <summary>
    /// Representa un mensaje en la conversación
    /// </summary>
    public class HablemosMessage
    {
        [JsonProperty("messageId")]
        public string MessageId { get; set; } = string.Empty;

        [JsonProperty("clientePrimeroTwinID")]
        public string ClientePrimeroID { get; set; } = string.Empty; 

        [JsonProperty("clienteSegundoID")]
        public string ClienteSegundoID { get; set; } = string.Empty; 

        [JsonProperty("duenoAppTwinID")]
        public string TwinID { get; set; } = string.Empty;

        [JsonProperty("duenoAppMicrosoftOID")]
        public string DuenoAppMicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("mensaje")]
        public string Mensaje { get; set; } = string.Empty;

        [JsonProperty("deQuien")]
        public string DeQuien { get; set; } = string.Empty;

        [JsonProperty("paraQuien")]
        public string ParaQuien { get; set; } = string.Empty;

        [JsonProperty("id")]
        public string id { get; set; } = string.Empty;

        [JsonProperty("fechaCreado")]
        public DateTime FechaCreado { get; set; }

        [JsonProperty("hora")]
        public string Hora { get; set; } = string.Empty;

        [JsonProperty("isRead")]
        public bool IsRead { get; set; }

        [JsonProperty("isDelivered")]
        public bool IsDelivered { get; set; }

        [JsonProperty("readAt")]
        public DateTime? ReadAt { get; set; }

        [JsonProperty("deliveredAt")]
        public DateTime? DeliveredAt { get; set; }


        [JsonProperty("vozPath")]
        public string vozPath { get; set; } = "";



        [JsonProperty("vozNombreArchivo")]
        public string VozNombreArchivo { get; set; } = "";

        [JsonProperty("ClientID")]
        public string ClientID { get; set; } = "";

        [JsonProperty("SASURLVOZ")]
        public string SASURLVOZ{ get; set; } = "";
    }

    /// <summary>
    /// Documento de conversación en Cosmos DB
    /// Un documento por par de usuarios
    /// </summary>
    public class HablemosConversation
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("pairId")]
        public string PairId { get; set; } = string.Empty;

        [JsonProperty("clientePrimeroID")]
        public string ClientePrimeroID { get; set; } = string.Empty;

        [JsonProperty("clienteSegundoID")]
        public string ClienteSegundoID { get; set; } = string.Empty;

        [JsonProperty("TwinID")]
        public string TwinID { get; set; } = string.Empty;

        [JsonProperty("duenoAppMicrosoftOID")]
        public string DuenoAppMicrosoftOID { get; set; } = string.Empty;

        [JsonProperty("mensajes")]
        public List<HablemosMessage> Mensajes { get; set; } = new();

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("lastActivityAt")]
        public DateTime LastActivityAt { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = "hablemos_conversation";

        [JsonProperty("vosPath")]
        public string VosPath { get; set; } = "";

        [JsonProperty("vozNombreArchivo")]
        public string vozNombreArchivo { get; set; } = "";

        [JsonProperty("ClientID")]
        public string ClientID { get; set; } = "";
    }

    #endregion

    #region Result Models

    public class EnviarMensajeResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public HablemosMessage? Mensaje { get; set; }
        public string? PairId { get; set; }
        public double RUConsumed { get; set; }
    }

    public class GetMensajesResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<HablemosMessage> Mensajes { get; set; } = new();
        public string Periodo { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public int TotalMensajes { get; set; }
        public double RUConsumed { get; set; }
    }

    public class HablemosMarkAsReadResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public int MarcadosCount { get; set; }
        public double RUConsumed { get; set; }
    }

    public class ObtenerConversacionesUsuarioResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<HablemosConversation> Conversaciones { get; set; } = new();
        public int TotalConversaciones { get; set; }
        public double RUConsumed { get; set; }
    }

    public class MejorarTextoResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string TextoOriginal { get; set; } = string.Empty;
        public string TextoMejorado { get; set; } = string.Empty;
        public string EstiloAplicado { get; set; } = string.Empty;
        public string? SerializedThread { get; set; }
        public int CaracteresOriginales { get; set; }
        public int CaracteresMejorados { get; set; }
    }

    #endregion
}

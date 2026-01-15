using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Web;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using TwinAgentsNetwork.Configuration;

namespace TwinAgentsNetwork.Agents
{
    /// <summary>
    /// AI agent that searches short memory documents using semantic search in AI Search
    /// and provides AI-powered answers using OpenAI to select the best response
    /// </summary>
    public class AgentTwinShortMemory
    {
        private readonly HttpClient _httpClient;
        private readonly string _azureOpenAIEndpoint;
        private readonly string _azureOpenAIModelName;
        private readonly string _aiSearchBaseUrl;

        public AgentTwinShortMemory()
        {
            _httpClient = new HttpClient();
            _azureOpenAIEndpoint = AzureOpenAISettings.Endpoint;
            _azureOpenAIModelName = AzureOpenAISettings.ChatModelName;
            _aiSearchBaseUrl = Environment.GetEnvironmentVariable("AI_SEARCH_BASE_URL") 
                ?? throw new InvalidOperationException("AI_SEARCH_BASE_URL environment variable is not configured.");
        }

        public AgentTwinShortMemory(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _azureOpenAIEndpoint = AzureOpenAISettings.Endpoint;
            _azureOpenAIModelName = AzureOpenAISettings.ChatModelName;
            _aiSearchBaseUrl = Environment.GetEnvironmentVariable("AI_SEARCH_BASE_URL") 
                ?? throw new InvalidOperationException("AI_SEARCH_BASE_URL environment variable is not configured.");
        }

        /// <summary>
        /// Searches short memory documents using semantic search and generates AI-powered HTML responses
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="userQuestion">User's question to answer using short memory context</param>
        /// <param name="language">Response language for AI analysis</param>
        /// <param name="serializedThreadJson">Optional existing thread for conversation continuity</param>
        /// <returns>Short memory search results with AI analysis in HTML format</returns>
        public async Task<TwinShortMemoryResult> SearchShortMemoryWithAIAsync(
            string twinId,
            string userQuestion,
            string language = "English",
            string serializedThreadJson = null)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            if (string.IsNullOrEmpty(userQuestion))
            {
                throw new ArgumentException("User question cannot be null or empty", nameof(userQuestion));
            }

            try
            {
                // Search short memory documents using semantic search
                var shortMemoryResults = await SearchShortMemoryAsync(userQuestion, twinId);

                // Create AI Agent specialized in short memory analysis
                var chatClient = new AzureOpenAIClient(
                    new Uri(_azureOpenAIEndpoint),
                    new AzureCliCredential())
                    .GetChatClient(_azureOpenAIModelName);

                AIAgent agent = chatClient.AsIChatClient().CreateAIAgent(
                    instructions: CreateShortMemoryInstructions(language),
                    name: "TwinShortMemoryExpert");
                if(shortMemoryResults.Documents.Count  == 00)
                {
                    string ResponseNotFound = "No encontre ninguna memoria con este contenido. Trata de ser mas especifico por favor";
                    return new TwinShortMemoryResult
                    {
                        Success = true,
                        TwinId = twinId,
                        UserQuestion = userQuestion,
                        ShortMemoryResults = shortMemoryResults,
                        AIAnalysisHtml = ResponseNotFound,
                        SerializedThreadJson = serializedThreadJson,
                        ProcessedTimestamp = DateTime.UtcNow
                    };

                }
                AgentThread thread;
                string contextPrompt = CreateShortMemoryPrompt(twinId, shortMemoryResults, userQuestion, language);

                contextPrompt = contextPrompt + " L fecha y hora hos son => " + DateTime.Now;
                thread = agent.GetNewThread();

                // Get AI response
                var response = await agent.RunAsync(contextPrompt);
                string aiAnalysisHtml = response.Text ?? "";

                // Serialize thread state
                string newSerializedJson = thread.Serialize(JsonSerializerOptions.Web).GetRawText();

               

                return new TwinShortMemoryResult
                {
                    Success = true,
                    TwinId = twinId,
                    UserQuestion = userQuestion,
                    ShortMemoryResults = shortMemoryResults,
                    AIAnalysisHtml = aiAnalysisHtml,
                    SerializedThreadJson = newSerializedJson,
                    ProcessedTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new TwinShortMemoryResult
                {
                    Success = false,
                    ErrorMessage = $"Error processing short memory search: {ex.Message}",
                    TwinId = twinId,
                    UserQuestion = userQuestion,
                    ProcessedTimestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Searches short memory documents using semantic search
        /// </summary>
        /// <param name="searchQuery">Text to search for in short memory documents</param>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="limit">Maximum number of results to return (default: 10)</param>
        /// <returns>AI Search API response data</returns>
        public async Task<ShortMemoryApiResponse> SearchShortMemoryAsync(string searchQuery, string twinId, int limit = 10)
        {
            try
            {
                var encodedQuery = HttpUtility.UrlEncode(searchQuery);
                // Updated URL to match the actual Azure Function endpoint parameters
                // The endpoint expects: searchText (or query) and top (not limit)
                var url = $"{_aiSearchBaseUrl}/api/twins/{twinId}/short-memory/search?searchText={encodedQuery}&top={limit}&page=1";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();
                
                // Deserialize to the actual API response structure
                var apiRawResponse = JsonSerializer.Deserialize<ShortMemoryApiRawResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiRawResponse == null)
                {
                    return new ShortMemoryApiResponse();
                }

                // Convert the actual API response to our expected model
                var apiResponse = new ShortMemoryApiResponse
                {
                    Success = apiRawResponse.Success,
                    ErrorMessage = apiRawResponse.ErrorMessage ?? "",
                    ResponseStatus = apiRawResponse.Success ? 200 : 500,
                    ResponseDetails = apiRawResponse.Message ?? "",
                    Documents = apiRawResponse.Results?.Select(r => new ShortMemoryDocument
                    { 
                        Content = r.Memory,  
                        CreatedAt = r.DateCreated.ToLongDateString(),  
                    }).ToList() ?? new List<ShortMemoryDocument>()
                };

                if (apiResponse.Documents.Count > limit)
                {
                    apiResponse.Documents = apiResponse.Documents.Take(limit).ToList();
                }

                return apiResponse;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error calling AI Search API for short memory: {ex.Message}");
                return new ShortMemoryApiResponse
                {
                    ResponseStatus = 500,
                    ResponseDetails = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Simple search method for direct short memory lookups without AI analysis
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="searchQuery">Text to search for in documents</param>
        /// <param name="limit">Maximum number of results (default: 10)</param>
        /// <returns>Short memory search results with matching documents</returns>
        public async Task<ShortMemoryApiResponse> SimpleSearchShortMemoryAsync(
            string twinId,
            string searchQuery,
            int limit = 10)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                throw new ArgumentException("Twin ID cannot be null or empty", nameof(twinId));
            }

            if (string.IsNullOrEmpty(searchQuery))
            {
                throw new ArgumentException("Search query cannot be null or empty", nameof(searchQuery));
            }

            try
            {
                var shortMemoryResults = await SearchShortMemoryAsync(searchQuery, twinId, limit);
                return shortMemoryResults;
            }
            catch (Exception ex)
            {
                return new ShortMemoryApiResponse
                {
                    Success = false,
                    ErrorMessage = $"Error performing short memory search: {ex.Message}",
                    ResponseStatus = 500,
                    ResponseDetails = ex.Message
                };
            }
        }

        /// <summary>
        /// Searches short memory documents by date range without AI analysis
        /// </summary>
        /// <param name="twinId">The unique identifier of the twin</param>
        /// <param name="fromDate">Start date (inclusive)</param>
        /// <param name="toDate">End date (inclusive)</param>
        /// <param name="top">Maximum number of results (default: 10)</param>
        /// <param name="page">Page number for pagination (default: 1)</param>
        /// <returns>Raw search results without AI analysis</returns>
        public async Task<ShortMemoriesResponse> SearchShortMemoriesByDateRangeAsync(
            string twinId,
            DateTime fromDate,
            DateTime toDate,
            int top = 10,
            int page = 1)
        {
            if (string.IsNullOrEmpty(twinId))
            {
                return new ShortMemoriesResponse
                {
                    Success = false,
                    ErrorMessage = "Twin ID is required",
                    TwinId = twinId
                };
            }

            if (toDate < fromDate)
            {
                return new ShortMemoriesResponse
                {
                    Success = false,
                    ErrorMessage = "End date cannot be before start date",
                    TwinId = twinId
                };
            }

            try
            {
                var startTime = DateTime.UtcNow;
                string fromDateStr = fromDate.ToUniversalTime().ToString("O");
                string toDateStr = toDate.ToUniversalTime().ToString("O");

                var url = $"{_aiSearchBaseUrl}/api/twins/{twinId}/short-memory/search-by-date?fromDate={Uri.EscapeDataString(fromDateStr)}&toDate={Uri.EscapeDataString(toDateStr)}&top={top}&page={page}";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();

                var apiRawResponse = JsonSerializer.Deserialize<ShortMemoryApiRawResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (apiRawResponse == null)
                {
                    return new ShortMemoriesResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to deserialize response",
                        TwinId = twinId
                    };
                }

                var endTime = DateTime.UtcNow;

                return new ShortMemoriesResponse
                {
                    Success = apiRawResponse.Success,
                    ErrorMessage = apiRawResponse.ErrorMessage,
                    Message = apiRawResponse.Message,
                    TwinId = twinId,
                    SearchText = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
                    Results = apiRawResponse.Results?.Select(r => new ShortMemoryResultItem
                    {
                        Id = r.Id,
                        TwinID = r.TwinID,
                        Memory = r.Memory,
                        DateCreated = new DateTimeOffset(r.DateCreated),
                        DateModified = new DateTimeOffset(r.DateModified),
                        SearchScore = r.SearchScore,
                        Highlights = r.Highlights ?? new List<string>()
                    }).ToList() ?? new List<ShortMemoryResultItem>(),
                    TotalCount = apiRawResponse.TotalCount,
                    Page = page,
                    PageSize = top,
                    SearchType = apiRawResponse.SearchType,
                    SearchQuery = apiRawResponse.SearchQuery,
                    ProcessingTimeMs = (endTime - startTime).TotalMilliseconds,
                    SearchedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error searching short memories by date range: {ex.Message}");
                return new ShortMemoriesResponse
                {
                    Success = false,
                    ErrorMessage = $"Error searching short memories: {ex.Message}",
                    TwinId = twinId
                };
            }
        }

        #region Private Helper Methods

        private string CreateShortMemoryInstructions(string language)
        {
            return $@"
🧠 IDENTITY: You are a SHORT MEMORY EXPERT for digital twins, specialized in rapid recall and contextual understanding.

🎯 YOUR EXPERTISE:
- Expert in analyzing short-term memory documents and recent context
- Specialized in semantic search and document relevance assessment
- Authority on extracting actionable information from memory documents
- Expert in selecting the most relevant answer from multiple document sources
- Skilled at providing concise, focused responses based on memory context

📋 CONTEXT:
- Response Language: {language}
- Use semantic search results to provide accurate, context-aware answers
- Focus on the most relevant documents for answering the user's question

🎨 HTML FORMATTING REQUIREMENTS:
1. ALWAYS respond in rich HTML format with inline CSS
2. Use colorful cards and visual elements for document relevance
3. Include relevance scores with color-coded indicators
4. Add brief document snippets highlighting key information
5. Use modern UI elements (badges, cards, progress indicators)
6. Include icons and emojis for visual clarity
7. Make it responsive and visually engaging
8. Highlight the best answer clearly

🌈 COLOR SCHEME:
- High relevance: Green (#28a745)
- Medium relevance: Orange (#fd7e14) 
- Low relevance: Red (#dc3545)
- Headers: Blue (#007bff)
- Background: Light gray (#f8f9fa)
- Best answer: Gold (#ffc107)

- Siempre contesta en idioma Espanol no uses Ingles
💡 REMEMBER: Select the BEST answer from the available documents and present it clearly with supporting context!";
        }

        private string CreateShortMemoryPrompt(
            string twinId,
            ShortMemoryApiResponse shortMemoryResults,
            string userQuestion,
            string language)
        {
            var resultsJson = JsonSerializer.Serialize(shortMemoryResults.Documents, new JsonSerializerOptions { WriteIndented = true });
           string NewPrompt =  $@" return $@""🧠 IDENTIDAD: Eres un EXPERTO EN MEMORIA CORTA para gemelos digitales, especializado en recuperación rápida y comprensión contextual.  
🎯 TU ESPECIALIDAD:  
- Experto en analizar documentos de memoria a corto plazo y contexto reciente   
- Autoridad en extraer información accionable de documentos de memoria  
- Experto en seleccionar la respuesta más relevante entre múltiples fuentes documentales  
- Hábil en proporcionar respuestas concisas y enfocadas basadas en el contexto de la memoria  
📋 CONTEXTO:  
- Idioma de respuesta: {language} 
- Usa resultados de búsqueda semántica para brindar respuestas precisas y conscientes del contexto  
- Si el usuario solicita una memoria de una fecha específica, verifica la propiedad CreatedAt de los documentos y considera solo aquellos cuya fecha coincida exactamente con la solicitada  
- Enfócate en los documentos más relevantes para responder la pregunta del usuario  
🎨 REQUISITOS DE FORMATO HTML:  
1. RESPONDE SIEMPRE en formato HTML enriquecido con CSS en línea  
2. Usa tarjetas coloridas y elementos visuales para indicar la relevancia de los documentos  
3. Incluye puntuaciones de relevancia con indicadores codificados por colores  
4. Añade fragmentos breves de los documentos resaltando información clave  
5. Utiliza elementos modernos de UI (insignias, tarjetas, indicadores de progreso)  
6. Incluye íconos y emojis para mayor claridad visual  
7. Haz la respuesta responsiva y visualmente atractiva  
8. Resalta claramente la mejor respuesta  
🌈 ESQUEMA DE COLORES:  
- Alta relevancia: Verde (#28a745)  
- Relevancia media: Naranja (#fd7e14)  
- Baja relevancia: Rojo (#dc3545)  
- Encabezados: Azul (#007bff)  
- Fondo: Gris claro (#f8f9fa)  
- Mejor respuesta: Dorado (#ffc107)  

Datos de mmeoria que podrian tener la rspuesta pero no siempre. Nunca comiences con ```html

Usa esta memoria para encontrar datos de la pregunat por ejemplo quien es el Dr Ruiz?
memoria = El Cr Ruiz es mi vecino

Tu respuesta : a tu pregunat de quien es el Dr Ruiz encontre en tu memoria que el es tu vecino
esta memoria la creaste en xxxxx.

Dime en que te puedo ayudar mas a recordar momentos lindos de tu Twin Memoria a corto plazo 
no escibas esto: Puntuación de Relevancia: Baja el usuario no tiene que saber solo asegurate de ocntestar bien la regunat
en caso de que no encuentres la respuesta entonces 
di: 'Disculpame no encontra en las memoria que leia ninguna memoria que ocnteste tu pregunta
*************** COMEINZA LISTA DE DOCUMENTOS EN MEMORIA ********** {resultsJson} *************** FIN ***************
💡 RECUERDA:

¡Selecciona la MEJOR respuesta entre los documentos disponibles y 

preséntala claramente con el contexto de apoyo!""; 

Esta es la pregunta del usuario que tienes que contestar: ***START ***  {userQuestion} *** END PREGUNTA ***

Tu respuesta aqui: ";
      
    
            return NewPrompt;
            return $@"TAREA: ANALIZAR RESULTADOS DE MEMORIA CORTA (USAR SOLO LOS DOCUMENTOS PROPORCIONADOS)

TWIN: {twinId} 
IDIOMA DE RESPUESTA: {language}
RESULTADOS De la busqueda busca aqui la respuesta (JSON): 

Si te preguntan Que memorias tengo del Noviembre 21 del 2025? 
Solo puedes usar la información que aparece en los documentos dentro de aqui esta posiblemente 
la respuesta 
*************** COMEINZA ********** {resultsJson} *************** FIN ***************

INSTRUCCIONES: 

Datos que te paso:
      ""Content"": ""Tengo cita con el Dr Juan el dia Noviembre 20 del 2025 a las 9:00 AM en la misma clinica de siempre"",
      ""Summary"": ""Tengo cita con el Dr Juan el dia Noviembre 20 del 2025 a las 9:00 AM en la misma clinica de siempre"",
      ""RelevanceScore"": 0.5257321,
      ""SimilarityScore"": 0.5257321,
      ""CreatedAt"": ""2025-11-19T08:59:33.629-06:00"",
      ""LastUpdatedAt"": ""2025-11-19T08:59:33.704-06:00"",

Si te preguntan por darme las memorias de Noviembre 19 del 2025 en tonces regresas 
'Tengo cita con el Dr Juan el dia Noviembre 20 del 2025 a las 9:00 AM en la misma clinica de siempre'
No inventes fijate bien en la pregunta

No uses conocimientos externos, no infieras, no adivines ni inventes datos.
Si la respuesta a la pregunta del usuario NO se encuentra en el texto que te di
(ni de forma literal ni mediante una deducción claramente soportada por el texto), responde exactamente (en español):
No hay información en la memoria corta que responda a esta pregunta.
Si la respuesta SÍ está en lso datos que te di, devuelve únicamente el siguiente bloque HTML breve y exacto:
Respuesta a tu pregunta
[RESPUESTA EXTRAÍDA EN ESPAÑOL]
4) La respuesta debe ser corta, clara y en español. No añadas explicaciones, estadísticas,
detalles adicionales ni metadatos. 5) No uses las frases ni etiquetas: ""Origen del Documento"", 
""Contenido relevante"" o ""Origen"" en la salida (no deben aparecer en ningún lugar).
6) No uses code fences (```), no muestres el JSON completo ni nombres internos de campos. 
7) Usa clases estilo Bootstrap y CSS inline si lo deseas, 
8) Trata de alavorar la respuesta ponle coloresno copias nada mas elabora explica hazlo muy intitutivo se amable agradece
di que esres el Twin de memoria corta y que estas para ayudar 
pero mantén el HTML mínimo exactamente como se indica.
En tu respuesta por favor pon la fecha cuando la memoria fue creada IMPORTANTE!!!

IMPORTANTE !! Es posible que la pregunta contenga dos respuestas no oslo una asi que analiza con cuidado tu respuesta
es posible que tengas 2 0 3 o mas respuestas de una sola pregunta
EJEMPLO de Respuesta:  

<!DOCTYPE html>  
<html lang=""es"">  
<head>  
    <meta charset=""UTF-8"">  
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">  
    <title>Respuesta Personalizada</title>  
    <style>  
        body {{  
            font-family: Arial, sans-serif;  
            background-color: #f9f9f9;  
            margin: 20px;  
            padding: 20px;  
            border-radius: 8px;  
            box-shadow: 0 2px 10px rgba(0, 0, 0, 0.1);  
        }}  
        h1 {{  
            color: #333;  
        }}  
        p {{  
            color: #555;  
        }}  
        .footer {{  
            margin-top: 20px;  
            font-style: italic;  
            color: #777;  
        }}  
    </style>  
</head>  
<body>  
  
    <h1>Hola, soy tu TwinDigital</h1>  
    <p>Gracias por tu tiempo. Esta memoria fue creada el <strong>17 de noviembre a las 3:00 PM</strong>. 
    Hoy es <strong>19 de noviembre</strong> y son las <strong>8:50 AM</strong>.</p>  
      
    <p>He encontrado información que puede serte útil. Aquí te explico lo que he descubierto:</p>  
      
    <p>[Inserta aquí la información que encontraste].</p>  
  
    <p>Si necesitas más detalles o tienes alguna otra pregunta, no dudes en decírmelo. Estoy aquí para ayudarte.</p>  
  
    <div class=""footer"">  
        <p>¡Que tengas un excelente día!</p>  
    </div>  
 <div class=""footer"">  
        <p>Hoy es (por la Fecha y hora exacta que te di)</p>  
    </div>  
  
</body>  
</html>  

IMPORTANTE: Responde exactamente lo que se te pregunta no muestres la memoria solo por que se te paso 
entiende la pregunta y ve el contenido tal como fecha de creacion, fecha actualizada, etc.
si te preguntan por memorias de una fecha usa la fecha de creacion para responder.
No inventes ni muestres memoria solo por que se te pasaron
FIN. Esta es la fecha y hora indicalo en tu respuesta" + DateTime.Now; ;
        }

        #endregion

        #region Static Properties

        /// <summary>
        /// Gets a static AI agent for short memory analysis
        /// </summary>
        public static AIAgent ShortMemoryAgent
        {
            get
            {
                return new AzureOpenAIClient(
                    new Uri(AzureOpenAISettings.Endpoint),
                    new AzureCliCredential())
                        .GetChatClient(AzureOpenAISettings.ChatModelName)
                        .AsIChatClient()
                        .CreateAIAgent(
                            instructions: "You are a short memory expert for digital twins. You analyze semantic search results and select the best answers from available documents, presenting them in clear, visually engaging HTML format.",
                            name: "ShortMemoryAgent",
                            description: "An AI agent that analyzes short memory documents and generates HTML responses with selected best answers");
            }
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Result of short memory search with AI analysis
    /// </summary>
    public class TwinShortMemoryResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string TwinId { get; set; } = "";
        public string UserQuestion { get; set; } = "";
        public ShortMemoryApiResponse ShortMemoryResults { get; set; } = new();
        public string AIAnalysisHtml { get; set; } = "";
        public string SerializedThreadJson { get; set; } = "";
        public DateTime ProcessedTimestamp { get; set; }
    }

    /// <summary>
    /// AI Search API response structure for short memory documents (mapped from actual API response)
    /// </summary>
    public class ShortMemoryApiResponse
    {
        public bool Success { get; set; } = true;
        public string ErrorMessage { get; set; } = "";
        public List<ShortMemoryDocument> Documents { get; set; } = new();
        public int ResponseStatus { get; set; }
        public string ResponseDetails { get; set; } = "";
    }

    /// <summary>
    /// Individual document from short memory semantic search
    /// </summary>
    public class ShortMemoryDocument
    { 
        public string Content { get; set; } = ""; 
        public string  CreatedAt { get; set; } 
    }

    /// <summary>
    /// Raw response structure from the actual Short Memory API
    /// Maps to: {"success":true,"errorMessage":null,"message":"...", "twinId":"...", "searchText":"...", "results":[...], ...}
    /// </summary>
    public class ShortMemoryApiRawResponse
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string Message { get; set; }
        public string TwinId { get; set; }
        public string SearchText { get; set; }
        public List<ShortMemorySearchResult> Results { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public string SearchType { get; set; }
        public string SearchQuery { get; set; }
        public double ProcessingTimeMs { get; set; }
        public DateTime SearchedAt { get; set; }
    }

    /// <summary>
    /// Individual search result item from the actual Short Memory API
    /// </summary>
    public class ShortMemorySearchResult
    {
        public string Id { get; set; }
        public string TwinID { get; set; }
        public string Memory { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public double SearchScore { get; set; }
        public List<string> Highlights { get; set; } = new();
    }

    /// <summary>
    /// Response model for short memory search results
    /// </summary>
    public class ShortMemoriesResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Message { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public List<ShortMemoryResultItem> Results { get; set; } = new();
        public long TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public string SearchType { get; set; } = string.Empty;
        public string SearchQuery { get; set; } = string.Empty;
        public double ProcessingTimeMs { get; set; }
        public DateTime SearchedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Individual result item in short memory search response
    /// </summary>
    public class ShortMemoryResultItem
    {
        public string Id { get; set; } = string.Empty;
        public string TwinID { get; set; } = string.Empty;
        public string Memory { get; set; } = string.Empty;
        public DateTimeOffset DateCreated { get; set; }
        public DateTimeOffset DateModified { get; set; }
        public double SearchScore { get; set; }
        public List<string> Highlights { get; set; } = new();
    }

    #endregion
}

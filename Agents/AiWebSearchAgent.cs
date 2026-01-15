using Azure;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging; 
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Agents
{
    /// <summary>
    /// Agente especializado en búsquedas web inteligentes usando Azure AI Agents con Bing Grounding
    /// ========================================================================
    /// 
    /// Este agente utiliza Azure AI Agents con Bing Grounding para:
    /// - Realizar búsquedas web comprensivas sobre cualquier tema
    /// - Proporcionar información actualizada y relevante con fuentes
    /// - Procesar consultas de usuarios y retornar respuestas estructuradas
    /// - Utilizar Bing Search con grounding para información confiable
    /// 
    /// Author: TwinFx Project
    /// Date: January 15, 2025
    /// </summary>
    public class AiWebSearchAgent
    {
        private static readonly HttpClient client = new HttpClient();
        private readonly ILogger<AiWebSearchAgent> _logger;
        private readonly IConfiguration _configuration;


        // Azure AI Foundry configuration for Bing Grounding
        private const string PROJECT_ENDPOINT = "https://twinet-resource.services.ai.azure.com/api/projects/twinet";
        private const string MODEL_DEPLOYMENT_NAME = "gpt-4o";
        private const string BING_CONNECTION_ID = "/subscriptions/bf5f11e8-1b22-4e27-b55e-8542ff6dec42/resourceGroups/rg-jorgeluna-7911/providers/Microsoft.CognitiveServices/accounts/twinet-resource/projects/twinet/connections/twinbing";

        public AiWebSearchAgent(ILogger<AiWebSearchAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _logger.LogInformation("🌐 AiWebSearchAgent initialized for intelligent web search with Bing Grounding");
        }

        /// <summary>
        /// Realiza una búsqueda web inteligente usando Azure AI Agents con Bing Grounding
        /// Recibe el prompt del usuario con lo que quiere buscar
        /// </summary>
        /// <param name="userPrompt">Prompt del usuario con la consulta de búsqueda</param>
        /// <returns>Respuesta estructurada con información web encontrada en formato JSON</returns>
        public async Task<string> BingGroundingSearchAsync(string userPrompt)
        {
            SearchResults Search = new SearchResults();
            _logger.LogInformation("🔧 Attempting Bing Grounding Search with Azure AI Agents");
            _logger.LogInformation("🔍 User prompt: {UserPrompt}", userPrompt);

            try
            {
                // Validar entrada
                if (string.IsNullOrEmpty(userPrompt))
                {
                    _logger.LogWarning("⚠️ Empty user prompt provided");
                    return "⚠️ Empty user prompt provided";
                }

                // Step 1: Create a client object
                var agentClient = new PersistentAgentsClient(PROJECT_ENDPOINT, new DefaultAzureCredential());

                // Step 2: Create an Agent with the Grounding with Bing search tool enabled
                var bingGroundingTool = new BingGroundingToolDefinition(
                    new BingGroundingSearchToolParameters(
                        [new BingGroundingSearchConfiguration(BING_CONNECTION_ID)]
                    )
                );

                var agent = await agentClient.Administration.CreateAgentAsync(
                    model: MODEL_DEPLOYMENT_NAME,
                    name: "web-search-agent",
                    instructions: "Use the bing grounding tool to search for comprehensive web information. Provide detailed, accurate information with sources. Focus on current and relevant information. Always include citations and links when available.",
                    tools: [bingGroundingTool]
                );

                _logger.LogInformation("✅ Azure AI Agent created successfully");

                // Step 3: Create a thread and run with enhanced prompt
                var thread = await agentClient.Threads.CreateThreadAsync();

                var message = await agentClient.Messages.CreateMessageAsync(
                    thread.Value.Id,
                    MessageRole.User,
                    $"Search for comprehensive web information about: {userPrompt}. Provide detailed, accurate information with sources and citations.");

                var run = await agentClient.Runs.CreateRunAsync(thread.Value.Id, agent.Value.Id);

                _logger.LogInformation("🚀 Search run initiated, waiting for completion...");

                // Step 4: Wait for the agent to complete
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                    run = await agentClient.Runs.GetRunAsync(thread.Value.Id, run.Value.Id);
                }
                while (run.Value.Status == RunStatus.Queued || run.Value.Status == RunStatus.InProgress);

                if (run.Value.Status != RunStatus.Completed)
                {
                    var errorMessage = $"Bing Grounding run failed: {run.Value.LastError?.Message}";
                    _logger.LogError("❌ {ErrorMessage}", errorMessage);
                    return errorMessage;
                }

                _logger.LogInformation("✅ Search run completed successfully");

                // Step 5: Retrieve and process the messages
                var messages = agentClient.Messages.GetMessagesAsync(
                    threadId: thread.Value.Id,
                    order: ListSortOrder.Ascending
                );

                var searchResults = new List<string>();

                await foreach (var threadMessage in messages)
                {
                    if (threadMessage.Role != MessageRole.User)
                    {
                        foreach (var contentItem in threadMessage.ContentItems)
                        {
                            if (contentItem is MessageTextContent textItem)
                            {
                                string response = textItem.Text;

                                // Process annotations and citations
                                if (textItem.Annotations != null)
                                {
                                    foreach (var annotation in textItem.Annotations)
                                    {
                                        if (annotation is MessageTextUriCitationAnnotation urlAnnotation)
                                        {
                                            response = response.Replace(urlAnnotation.Text,
                                                $" [{urlAnnotation.UriCitation.Title}]({urlAnnotation.UriCitation.Uri})");
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(response))
                                {
                                    searchResults.Add(response);
                                }
                            }
                        }
                    }
                }

                // Step 6: Clean up resources
                try
                {
                    await agentClient.Threads.DeleteThreadAsync(threadId: thread.Value.Id);
                    await agentClient.Administration.DeleteAgentAsync(agentId: agent.Value.Id);
                    _logger.LogInformation("🧹 Azure AI Agent resources cleaned up successfully");
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "⚠️ Warning during cleanup of Azure AI Agent resources");
                }

                // Step 7: Validate and return results
                if (searchResults.Count == 0)
                {
                    var noResultsMessage = "No se encontraron resultados en la búsqueda web";
                    _logger.LogWarning("📭 {NoResultsMessage} for prompt: {UserPrompt}", noResultsMessage, userPrompt);
                    return "No se encontraron resultados en la búsqueda web";
                }
                var aiResponse = searchResults[0];
                //  var searchResultsData = JsonConvert.DeserializeObject<SearchResults>(aiResponse);
                return aiResponse;
            }





            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during web search for prompt: {UserPrompt}", userPrompt);
                return ex.Message;
            }
        }




        public async Task<string> GoolgSearchOnly(string Question)
        {
            SearchResults searchResultsData = new SearchResults();
            string apiKey = "AIzaSyCbH7BdKombRuTBAOavP3zX4T8pw5eIVxo"; // Replace with your API key  
            string searchEngineId = "b07503c9152af4456"; // Replace with your Search Engine ID  
            string query = Question; // Replace with your search query  
            string Response = "";
            string url = $"https://www.googleapis.com/customsearch/v1?key={apiKey}&cx={searchEngineId}&q={Uri.EscapeDataString(query)}";

            try
            {
                var response = await client.GetStringAsync(url);
                return response;

            }
            catch (HttpRequestException e)
            {
                Console.WriteLine("Request error: " + e.Message);
            }

            return "Search FAILED";
        }


        /// <summary>
        /// Extrae solo los datos esenciales de Google Search para análisis de AI
        /// Optimizado para extraer TODAS las imágenes disponibles
        /// </summary>
        private SimpleGoogleSearchData ExtractEssentialSearchData(GoogleSearchResults searchResponse)
        {
            var simplifiedData = new SimpleGoogleSearchData
            {
                TotalResults = searchResponse?.SearchInformation?.TotalResults ?? "0",
                SearchTime = searchResponse?.SearchInformation?.SearchTime.ToString() ?? "0",
                Results = new List<SimpleSearchItem>()
            };

            if (searchResponse?.Items != null)
            {
                foreach (var item in searchResponse.Items.Take(5)) // Limitar a 10 resultados
                {
                    var simpleItem = new SimpleSearchItem
                    {
                        Title = item.Title ?? "",
                        Link = item.Link ?? "",
                        Snippet = item.Snippet ?? "",
                        DisplayLink = item.DisplayLink ?? "",
                        Images = new List<string>()
                    };

                    // EXTRACCIÓN COMPLETA DE IMÁGENES DE TODAS LAS FUENTES DISPONIBLES
                    if (item.PageMap != null)
                    {
                        // 1. Extraer de cse_thumbnail (thumbnails de Google)
                        if (item.PageMap.CseThumbnail != null)
                        {
                            foreach (var thumbnail in item.PageMap.CseThumbnail)
                            {
                                if (!string.IsNullOrEmpty(thumbnail.Src) &&
                                    !simpleItem.Images.Contains(thumbnail.Src))
                                {
                                    simpleItem.Images.Add(thumbnail.Src);
                                    _logger.LogDebug("🖼️ Added cse_thumbnail: {Src}", thumbnail.Src);
                                }
                            }
                        }

                        // 2. Extraer de cse_image (imágenes principales)
                        if (item.PageMap.CseImage != null)
                        {
                            foreach (var cseImage in item.PageMap.CseImage)
                            {
                                if (!string.IsNullOrEmpty(cseImage.Src) &&
                                    !simpleItem.Images.Contains(cseImage.Src) &&
                                    !cseImage.Src.StartsWith("x-raw-image://")) // Filtrar imágenes raw no utilizables
                                {
                                    simpleItem.Images.Add(cseImage.Src);
                                    _logger.LogDebug("🖼️ Added cse_image: {Src}", cseImage.Src);
                                }
                            }
                        }

                        // 3. Extraer de metatags (og:image y otras imágenes de metadatos)
                        if (item.PageMap.Metatags != null)
                        {
                            foreach (var metaTag in item.PageMap.Metatags)
                            {
                                // Extraer og:image
                                if (!string.IsNullOrEmpty(metaTag.OgImage) &&
                                    !simpleItem.Images.Contains(metaTag.OgImage))
                                {
                                    simpleItem.Images.Add(metaTag.OgImage);
                                    _logger.LogDebug("🖼️ Added og:image: {OgImage}", metaTag.OgImage);
                                }

                                // Extraer twitter:image
                                if (!string.IsNullOrEmpty(metaTag.TwitterImage) &&
                                    !simpleItem.Images.Contains(metaTag.TwitterImage))
                                {
                                    simpleItem.Images.Add(metaTag.TwitterImage);
                                    _logger.LogDebug("🖼️ Added twitter:image: {TwitterImage}", metaTag.TwitterImage);
                                }

                                // Buscar otras posibles imágenes en metadatos usando reflexión
                                var properties = typeof(MetaTag).GetProperties();
                                foreach (var prop in properties)
                                {
                                    var value = prop.GetValue(metaTag)?.ToString();
                                    if (!string.IsNullOrEmpty(value) &&
                                        IsImageUrl(value) &&
                                        !simpleItem.Images.Contains(value))
                                    {
                                        simpleItem.Images.Add(value);
                                        _logger.LogDebug("🖼️ Added meta image ({PropName}): {Value}", prop.Name, value);
                                    }
                                }
                            }
                        }
                    }

                    // 4. Buscar URLs de imágenes en el snippet del resultado
                    if (!string.IsNullOrEmpty(item.Snippet))
                    {
                        var imageUrlsInSnippet = ExtractImageUrlsFromText(item.Snippet);
                        foreach (var imgUrl in imageUrlsInSnippet)
                        {
                            if (!simpleItem.Images.Contains(imgUrl))
                            {
                                simpleItem.Images.Add(imgUrl);
                                _logger.LogDebug("🖼️ Added snippet image: {ImgUrl}", imgUrl);
                            }
                        }
                    }

                    // 5. Buscar URLs de imágenes en el título (raro, pero posible)
                    if (!string.IsNullOrEmpty(item.Title))
                    {
                        var imageUrlsInTitle = ExtractImageUrlsFromText(item.Title);
                        foreach (var imgUrl in imageUrlsInTitle)
                        {
                            if (!simpleItem.Images.Contains(imgUrl))
                            {
                                simpleItem.Images.Add(imgUrl);
                                _logger.LogDebug("🖼️ Added title image: {ImgUrl}", imgUrl);
                            }
                        }
                    }

                    // Log del total de imágenes encontradas para este resultado
                    _logger.LogInformation("📊 Result '{Title}': Found {ImageCount} images",
                        item.Title ?? "Unknown", simpleItem.Images.Count);

                    simplifiedData.Results.Add(simpleItem);
                }
            }

            // Log del total general
            var totalImages = simplifiedData.Results.Sum(r => r.Images.Count);
            _logger.LogInformation("🖼️ TOTAL IMAGES EXTRACTED: {TotalImages} across {ResultCount} results",
                totalImages, simplifiedData.Results.Count);

            return simplifiedData;
        }

        /// <summary>
        /// Determina si una URL es una imagen válida basándose en la extensión
        /// </summary>
        private bool IsImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.ToLowerInvariant();

                // Extensiones de imagen comunes
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tiff", ".tif" };

                return imageExtensions.Any(ext => path.EndsWith(ext)) ||
                       url.Contains("image") ||
                       url.Contains("photo") ||
                       url.Contains("picture") ||
                       url.Contains("img") ||
                       url.Contains("thumbnail");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extrae URLs de imágenes de un texto usando patrones regex
        /// </summary>
        private List<string> ExtractImageUrlsFromText(string text)
        {
            var imageUrls = new List<string>();

            if (string.IsNullOrEmpty(text)) return imageUrls;

            try
            {
                // Patrón regex para encontrar URLs de imágenes
                var urlPattern = @"https?://[^\s]+\.(?:jpg|jpeg|png|gif|webp|bmp|svg|ico|tiff|tif)(?:\?[^\s]*)?";
                var matches = System.Text.RegularExpressions.Regex.Matches(text, urlPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var url = match.Value;
                    if (!imageUrls.Contains(url))
                    {
                        imageUrls.Add(url);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error extracting image URLs from text");
            }

            return imageUrls;
        }


        public class ResultadoBusqueda
        {
            public string Titulo { get; set; }
            public string Contenido { get; set; }
            public string Fuente { get; set; }
            public string Url { get; set; }
            public string Relevancia { get; set; }
            public string FechaPublicacion { get; set; }
            public string Precios { get; set; }
            public string Categoria { get; set; }

            public string[] Fotos { get; set; }
        }

        public class LinkFuente
        {
            public string Titulo { get; set; }
            public string Url { get; set; }
            public string Descripcion { get; set; }
            public string TipoFuente { get; set; }
            public string Confiabilidad { get; set; }
        }

        public class DatosEspecificos
        {
            public List<string> Fechas { get; set; }
            public List<string> Numeros { get; set; }
            public List<string> Estadisticas { get; set; }
            public List<string> Precios { get; set; }
            public List<string> Ubicaciones { get; set; }
        }

        public class AnalisisContexto
        {
            public string Tendencias { get; set; }
            public string Impacto { get; set; }
            public string Perspectivas { get; set; }
            public string Actualidad { get; set; }
        }

        public class SearchResults
        {
            public string ResumenEjecutivo { get; set; }
            public string HtmlDetalles { get; set; }
            public List<ResultadoBusqueda> ResultadosBusqueda { get; set; }
            public List<LinkFuente> LinksYFuentes { get; set; }
            public DatosEspecificos DatosEspecificos { get; set; }
            public AnalisisContexto AnalisisContexto { get; set; }
            public List<string> Recomendaciones { get; set; }
            public List<string> PalabrasClave { get; set; }
            public string NivelConfianza { get; set; }
            public Dictionary<string, object> Metadatos { get; set; }
        }

        public class GoogleSearchResults
        {
            public string Kind { get; set; }

            public string ResponseHTML { get; set; }
            public Url Url { get; set; }
            public Queries Queries { get; set; }
            public Context Context { get; set; }
            public SearchInformation SearchInformation { get; set; }
            public Spelling Spelling { get; set; }
            public List<Item> Items { get; set; }
        }

        public class Url
        {
            public string Type { get; set; }
            public string Template { get; set; }
        }

        public class Queries
        {
            public List<Request> Request { get; set; }
            public List<NextPage> NextPage { get; set; }
        }

        public class Request
        {
            public string Title { get; set; }
            public string TotalResults { get; set; }
            public string SearchTerms { get; set; }
            public int Count { get; set; }
            public int StartIndex { get; set; }
            public string InputEncoding { get; set; }
            public string OutputEncoding { get; set; }
            public string Safe { get; set; }
            public string Cx { get; set; }
        }

        public class NextPage : Request
        {
            // Inherits all properties from Request  
        }

        public class Context
        {
            public string Title { get; set; }
        }

        public class SearchInformation
        {
            public double SearchTime { get; set; }
            public string FormattedSearchTime { get; set; }
            public string TotalResults { get; set; }
            public string FormattedTotalResults { get; set; }
        }

        public class Spelling
        {
            public string CorrectedQuery { get; set; }
            public string HtmlCorrectedQuery { get; set; }
        }

        public class Item
        {
            public string Kind { get; set; }
            public string Title { get; set; }
            public string HtmlTitle { get; set; }
            public string Link { get; set; }
            public string DisplayLink { get; set; }
            public string Snippet { get; set; }
            public string HtmlSnippet { get; set; }
            public string FormattedUrl { get; set; }
            public string HtmlFormattedUrl { get; set; }
            public PageMap PageMap { get; set; }
        }

        public class PageMap
        {
            [JsonProperty("hcard")]
            public List<HCard> HCard { get; set; }

            [JsonProperty("metatags")]
            public List<MetaTag> Metatags { get; set; }

            [JsonProperty("cse_thumbnail")]
            public List<CseThumbnail> CseThumbnail { get; set; }

            [JsonProperty("cse_image")]
            public List<CseImage> CseImage { get; set; }
        }

        public class HCard
        {
            [JsonProperty("fn")]
            public string Fn { get; set; }
        }

        public class MetaTag
        {
            [JsonProperty("referrer")]
            public string Referrer { get; set; }

            [JsonProperty("og:image")]
            public string OgImage { get; set; }

            [JsonProperty("theme-color")]
            public string ThemeColor { get; set; }

            [JsonProperty("og:image:width")]
            public string OgImageWidth { get; set; }

            [JsonProperty("og:type")]
            public string OgType { get; set; }

            [JsonProperty("viewport")]
            public string Viewport { get; set; }

            [JsonProperty("og:title")]
            public string OgTitle { get; set; }

            [JsonProperty("og:image:height")]
            public string OgImageHeight { get; set; }

            [JsonProperty("format-detection")]
            public string FormatDetection { get; set; }

            [JsonProperty("og:description")]
            public string OgDescription { get; set; }

            [JsonProperty("twitter:card")]
            public string TwitterCard { get; set; }

            [JsonProperty("og:site_name")]
            public string OgSiteName { get; set; }

            [JsonProperty("twitter:site")]
            public string TwitterSite { get; set; }

            [JsonProperty("twitter:image")]
            public string TwitterImage { get; set; }

            [JsonProperty("apple-itunes-app")]
            public string AppleItunesApp { get; set; }

            [JsonProperty("application-name")]
            public string ApplicationName { get; set; }

            [JsonProperty("apple-mobile-web-app-title")]
            public string AppleMobileWebAppTitle { get; set; }

            [JsonProperty("google")]
            public string Google { get; set; }

            [JsonProperty("og:locale")]
            public string OgLocale { get; set; }

            [JsonProperty("og:url")]
            public string OgUrl { get; set; }

            [JsonProperty("mobile-web-app-capable")]
            public string MobileWebAppCapable { get; set; }

            [JsonProperty("moddate")]
            public string ModDate { get; set; }

            [JsonProperty("creator")]
            public string Creator { get; set; }

            [JsonProperty("creationdate")]
            public string CreationDate { get; set; }

            [JsonProperty("producer")]
            public string Producer { get; set; }
        }

        public class CseThumbnail
        {
            [JsonProperty("src")]
            public string Src { get; set; }

            [JsonProperty("width")]
            public string Width { get; set; }

            [JsonProperty("height")]
            public string Height { get; set; }
        }

        public class CseImage
        {
            [JsonProperty("src")]
            public string Src { get; set; }
        }

        // ========================================
        // SIMPLIFIED GOOGLE SEARCH CLASSES - OPTIMIZED FOR AI ANALYSIS
        // ========================================

        /// <summary>
        /// Datos simplificados de Google Search optimizados para análisis de AI
        /// Contiene solo la información esencial para generar respuestas inteligentes
        /// </summary>
        public class SimpleGoogleSearchData
        {
            public string TotalResults { get; set; } = "0";
            public string SearchTime { get; set; } = "0";
            public List<SimpleSearchItem> Results { get; set; } = new List<SimpleSearchItem>();
        }

        /// <summary>
        /// Elemento de búsqueda simplificado con solo los datos más importantes
        /// </summary>
        public class SimpleSearchItem
        {
            public string Title { get; set; } = "";
            public string Link { get; set; } = "";
            public string Snippet { get; set; } = "";
            public string DisplayLink { get; set; } = "";
            public List<string> Images { get; set; } = new List<string>();
        }
    }
}
 
